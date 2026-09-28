using MuniClaw.Core.Data;
using MuniClaw.Core.Integrations;
using MuniClaw.Core.Models;

namespace MuniClaw.Core.Services;

public interface IPreviewDeploymentService
{
    Task<PreviewDeployment> TriggerPreviewAsync(Guid taskId, Guid actorUserId, CancellationToken ct);
    Task<PreviewDeployment?> GetPreviewAsync(Guid taskId, Guid actorUserId, CancellationToken ct);
    Task<PreviewDeployment> TearDownPreviewAsync(Guid taskId, Guid actorUserId, CancellationToken ct);
}

public sealed class PreviewDeploymentService : IPreviewDeploymentService
{
    private readonly IMuniClawStore _store;
    private readonly IOrganizationAuthorizationService _auth;
    private readonly IPreviewDeploymentClient _client;
    private readonly INotificationService? _notificationService;

    public PreviewDeploymentService(
        IMuniClawStore store,
        IOrganizationAuthorizationService auth,
        IPreviewDeploymentClient client,
        INotificationService? notificationService = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _auth = auth ?? throw new ArgumentNullException(nameof(auth));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _notificationService = notificationService;
    }

    public async Task<PreviewDeployment> TriggerPreviewAsync(Guid taskId, Guid actorUserId, CancellationToken ct)
    {
        if (!_store.Tasks.TryGetValue(taskId, out var task))
        {
            throw new KeyNotFoundException($"Task {taskId} was not found.");
        }

        if (!await _auth.CanAccessOrganizationAsync(actorUserId, task.OrganizationId, ct))
        {
            throw new UnauthorizedAccessException($"User {actorUserId} is not authorized for organization {task.OrganizationId}.");
        }

        // Idempotency: If preview already exists with status Active or Deploying, return existing
        var existing = _store.PreviewDeployments.Values
            .Where(p => p.TaskId == taskId && (p.Status == PreviewDeploymentStatus.Active || p.Status == PreviewDeploymentStatus.Deploying))
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefault();

        if (existing != null)
        {
            return existing;
        }

        var latestRun = _store.TaskRuns.Values
            .Where(r => r.TaskId == task.Id)
            .OrderByDescending(r => r.RunIndex)
            .FirstOrDefault();

        var delivery = _store.Deliveries.Values
            .Where(d => d.TaskId == task.Id)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefault();

        var commitSha = delivery?.ReviewedCommitSha ?? delivery?.PublishedCommitSha ?? task.BaseCommitSha;
        var serviceLabel = GetServiceLabel(task);
        var normalizedBranch = MinicloudPreviewDeploymentClient.NormalizeBranchName(task.TaskBranch);

        var preview = new PreviewDeployment
        {
            Id = Guid.NewGuid(),
            TaskId = task.Id,
            RunId = latestRun?.Id ?? Guid.Empty,
            OrganizationId = task.OrganizationId,
            BranchName = task.TaskBranch,
            NormalizedBranch = normalizedBranch,
            CommitSha = commitSha,
            Status = PreviewDeploymentStatus.Deploying,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _store.PreviewDeployments[preview.Id] = preview;
        PersistSnapshot();

        try
        {
            var previewUrl = await _client.TriggerDeploymentAsync(serviceLabel, task.TaskBranch, commitSha, ct);
            preview.PreviewUrl = previewUrl;
            preview.Status = PreviewDeploymentStatus.Active;
            preview.DeployedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            preview.Status = PreviewDeploymentStatus.Failed;
            preview.ErrorMessage = ex.Message;
        }
        finally
        {
            PersistSnapshot();
        }

        if (preview.Status == PreviewDeploymentStatus.Active && _notificationService != null)
        {
            try
            {
                var repoName = GetRepoName(task);
                var notification = new TaskLifecycleNotification
                {
                    EventType = NotificationEventType.DeliveryPublished,
                    TaskId = task.Id,
                    RunId = preview.RunId,
                    OrganizationId = task.OrganizationId,
                    TaskTitle = task.Title,
                    RepositoryName = repoName,
                    Branch = preview.BranchName,
                    Timestamp = DateTimeOffset.UtcNow,
                    Summary = $"Preview deployment active: {preview.PreviewUrl}",
                    ConsoleUrl = preview.PreviewUrl ?? string.Empty
                };

                await _notificationService.DispatchNotificationAsync(notification, ct);
            }
            catch
            {
                // Non-fatal if notifications fail
            }
        }

        return preview;
    }

    public async Task<PreviewDeployment?> GetPreviewAsync(Guid taskId, Guid actorUserId, CancellationToken ct)
    {
        if (!_store.Tasks.TryGetValue(taskId, out var task))
        {
            return null;
        }

        if (!await _auth.CanAccessOrganizationAsync(actorUserId, task.OrganizationId, ct))
        {
            throw new UnauthorizedAccessException($"User {actorUserId} is not authorized for organization {task.OrganizationId}.");
        }

        return _store.PreviewDeployments.Values
            .Where(p => p.TaskId == taskId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefault();
    }

    public async Task<PreviewDeployment> TearDownPreviewAsync(Guid taskId, Guid actorUserId, CancellationToken ct)
    {
        if (!_store.Tasks.TryGetValue(taskId, out var task))
        {
            throw new KeyNotFoundException($"Task {taskId} was not found.");
        }

        if (!await _auth.CanAccessOrganizationAsync(actorUserId, task.OrganizationId, ct))
        {
            throw new UnauthorizedAccessException($"User {actorUserId} is not authorized for organization {task.OrganizationId}.");
        }

        var preview = _store.PreviewDeployments.Values
            .Where(p => p.TaskId == taskId && p.Status != PreviewDeploymentStatus.TornDown)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefault();

        if (preview == null)
        {
            var anyPreview = _store.PreviewDeployments.Values
                .Where(p => p.TaskId == taskId)
                .OrderByDescending(p => p.CreatedAt)
                .FirstOrDefault();

            if (anyPreview != null)
            {
                return anyPreview;
            }

            throw new KeyNotFoundException($"No preview deployment found for task {taskId}.");
        }

        var serviceLabel = GetServiceLabel(task);
        await _client.TearDownDeploymentAsync(serviceLabel, preview.BranchName, ct);

        preview.Status = PreviewDeploymentStatus.TornDown;
        preview.TornDownAt = DateTimeOffset.UtcNow;
        PersistSnapshot();

        return preview;
    }

    private string GetServiceLabel(TaskEntity task)
    {
        if (_store.Projects.TryGetValue(task.ProjectId, out var project) && !string.IsNullOrWhiteSpace(project.Name))
        {
            return MinicloudPreviewDeploymentClient.NormalizeBranchName(project.Name);
        }
        return "app";
    }

    private string GetRepoName(TaskEntity task)
    {
        if (_store.Projects.TryGetValue(task.ProjectId, out var proj) &&
            _store.RepositoryConnections.TryGetValue(proj.RepositoryConnectionId, out var repo))
        {
            return repo.RepositoryFullName;
        }
        return string.Empty;
    }

    private void PersistSnapshot()
    {
        if (_store is MuniClawFileStore fileStore)
        {
            fileStore.SaveSnapshot();
        }
    }
}
