using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MuniClaw.Worker.Configuration;

namespace MuniClaw.Worker.Sandbox;

public sealed class TaskSandboxManager : ITaskSandboxManager
{
    private readonly WorkerOptions _options;
    private readonly ILogger<TaskSandboxManager> _logger;
    private readonly Func<string, long>? _diskSpaceProvider;

    private sealed class SandboxMetadata
    {
        public Guid TaskId { get; set; }
        public Guid RunId { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
        public DateTimeOffset? LastRunCompletedAtUtc { get; set; }
        public bool IsActive { get; set; }
    }

    public TaskSandboxManager(
        WorkerOptions options,
        ILogger<TaskSandboxManager>? logger = null,
        Func<string, long>? diskSpaceProvider = null)
    {
        _options = options;
        _logger = logger ?? NullLogger<TaskSandboxManager>.Instance;
        _diskSpaceProvider = diskSpaceProvider;
    }

    public async Task EnsureDiskSpaceAvailableAsync(long requiredBytes, CancellationToken ct)
    {
        long availableBytes;
        if (_diskSpaceProvider != null)
        {
            availableBytes = _diskSpaceProvider(_options.TasksRootPath);
        }
        else
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_options.TasksRootPath)) ?? "/");
                availableBytes = drive.AvailableFreeSpace;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to inspect drive free space. Assuming sufficient space.");
                availableBytes = long.MaxValue;
            }
        }

        if (availableBytes < requiredBytes)
        {
            throw new InvalidOperationException(
                $"Disk pressure admission rejected: host available free space ({availableBytes} bytes) is below required minimum ({requiredBytes} bytes).");
        }

        await Task.CompletedTask;
    }

    public async Task<TaskSandboxContext> CreateOrGetSandboxAsync(Guid taskId, Guid runId, CancellationToken ct)
    {
        await EnsureDiskSpaceAvailableAsync(_options.MinFreeDiskSpaceBytes, ct);

        var taskRoot = Path.Combine(_options.TasksRootPath, taskId.ToString("N"));
        var worktreeDir = Path.Combine(taskRoot, "worktree");
        var homeDir = Path.Combine(taskRoot, "home");
        var tmpDir = Path.Combine(taskRoot, "tmp");

        Directory.CreateDirectory(taskRoot);
        Directory.CreateDirectory(worktreeDir);
        Directory.CreateDirectory(homeDir);
        Directory.CreateDirectory(tmpDir);

        var metaPath = Path.Combine(taskRoot, "sandbox.json");
        SandboxMetadata metadata;

        if (File.Exists(metaPath))
        {
            var json = await File.ReadAllTextAsync(metaPath, ct);
            metadata = JsonSerializer.Deserialize<SandboxMetadata>(json) ?? new SandboxMetadata();
            metadata.RunId = runId;
            metadata.IsActive = true;
        }
        else
        {
            metadata = new SandboxMetadata
            {
                TaskId = taskId,
                RunId = runId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                IsActive = true,
                LastRunCompletedAtUtc = null
            };
        }

        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct);

        return new TaskSandboxContext
        {
            TaskId = taskId,
            RunId = runId,
            RootDirectory = taskRoot,
            WorktreeDirectory = worktreeDir,
            HomeDirectory = homeDir,
            TmpDirectory = tmpDir,
            CreatedAtUtc = metadata.CreatedAtUtc,
            LastRunCompletedAtUtc = metadata.LastRunCompletedAtUtc,
            IsActive = true,
            Limits = SandboxResourceLimits.Default
        };
    }

    public async Task MarkRunCompletedAsync(Guid taskId, Guid runId, bool isSuccess, DateTimeOffset completedAt, CancellationToken ct)
    {
        var taskRoot = Path.Combine(_options.TasksRootPath, taskId.ToString("N"));
        var metaPath = Path.Combine(taskRoot, "sandbox.json");

        if (!File.Exists(metaPath))
        {
            return;
        }

        var json = await File.ReadAllTextAsync(metaPath, ct);
        var metadata = JsonSerializer.Deserialize<SandboxMetadata>(json);
        if (metadata != null)
        {
            metadata.IsActive = false;
            metadata.LastRunCompletedAtUtc = completedAt;
            await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), ct);
        }
    }

    public async Task<IReadOnlyList<Guid>> CleanupExpiredSandboxesAsync(DateTimeOffset now, CancellationToken ct)
    {
        var cleaned = new List<Guid>();

        if (!Directory.Exists(_options.TasksRootPath))
        {
            return cleaned;
        }

        var retentionSpan = TimeSpan.FromDays(_options.RetentionDays);

        foreach (var taskDir in Directory.GetDirectories(_options.TasksRootPath))
        {
            ct.ThrowIfCancellationRequested();

            var metaPath = Path.Combine(taskDir, "sandbox.json");
            if (!File.Exists(metaPath))
            {
                continue;
            }

            try
            {
                var json = await File.ReadAllTextAsync(metaPath, ct);
                var metadata = JsonSerializer.Deserialize<SandboxMetadata>(json);
                if (metadata == null)
                {
                    continue;
                }

                // Invariant: Cleanup never silently erases active work!
                if (metadata.IsActive)
                {
                    continue;
                }

                if (metadata.LastRunCompletedAtUtc.HasValue)
                {
                    var age = now - metadata.LastRunCompletedAtUtc.Value;
                    if (age > retentionSpan)
                    {
                        _logger.LogInformation("Cleaning up expired task sandbox {TaskId} (age: {AgeDays:F1} days)", metadata.TaskId, age.TotalDays);
                        Directory.Delete(taskDir, recursive: true);
                        cleaned.Add(metadata.TaskId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process sandbox retention check for {TaskDir}", taskDir);
            }
        }

        return cleaned;
    }

    public Task DeleteSandboxAsync(Guid taskId, CancellationToken ct)
    {
        var taskRoot = Path.Combine(_options.TasksRootPath, taskId.ToString("N"));
        if (Directory.Exists(taskRoot))
        {
            Directory.Delete(taskRoot, recursive: true);
        }
        return Task.CompletedTask;
    }

    public void ValidatePathAccess(Guid taskId, string requestedPath)
    {
        var taskRoot = Path.Combine(_options.TasksRootPath, taskId.ToString("N"));
        SandboxPolicyEnforcer.ValidatePathAccess(taskRoot, requestedPath);
    }

    public void ValidateNetworkTarget(string hostOrIp)
    {
        SandboxPolicyEnforcer.ValidateNetworkTarget(hostOrIp);
    }

    public IDictionary<string, string> GetSanitizedEnvironment(TaskSandboxContext sandbox)
    {
        return SandboxPolicyEnforcer.SanitizeEnvironment(sandbox.HomeDirectory, sandbox.TmpDirectory);
    }
}
