namespace MuniClaw.Worker.Sandbox;

public sealed record TaskSandboxContext
{
    public required Guid TaskId { get; init; }
    public required Guid RunId { get; init; }
    public required string RootDirectory { get; init; }
    public required string WorktreeDirectory { get; init; }
    public required string HomeDirectory { get; init; }
    public required string TmpDirectory { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? LastRunCompletedAtUtc { get; set; }
    public bool IsActive { get; set; }
    public required SandboxResourceLimits Limits { get; init; }
}

public interface ITaskSandboxManager
{
    Task<TaskSandboxContext> CreateOrGetSandboxAsync(Guid taskId, Guid runId, CancellationToken ct);
    Task MarkRunCompletedAsync(Guid taskId, Guid runId, bool isSuccess, DateTimeOffset completedAt, CancellationToken ct);
    Task<IReadOnlyList<Guid>> CleanupExpiredSandboxesAsync(DateTimeOffset now, CancellationToken ct);
    Task DeleteSandboxAsync(Guid taskId, CancellationToken ct);
    void ValidatePathAccess(Guid taskId, string requestedPath);
    void ValidateNetworkTarget(string hostOrIp);
    IDictionary<string, string> GetSanitizedEnvironment(TaskSandboxContext sandbox);
    Task EnsureDiskSpaceAvailableAsync(long requiredBytes, CancellationToken ct);
}
