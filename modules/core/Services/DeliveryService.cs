using MuniClaw.Core.Contracts.Integrations;
using MuniClaw.Core.Contracts.Tasks;
using MuniClaw.Core.Data;
using MuniClaw.Core.Models;

namespace MuniClaw.Core.Services;

public sealed record PublishDeliveryRequest
{
    public required Guid OrganizationId { get; init; }
    public required Guid TaskId { get; init; }
    public required Guid RunId { get; init; }
    public required Guid UserId { get; init; }
    public required string BaseCommitSha { get; init; }
    public required string ReviewedCommitSha { get; init; }
    public required string TargetBranch { get; init; }
    public required string Title { get; init; }
    public required string Body { get; init; }
}

public interface IDeliveryService
{
    Task<DeliveryRecord> PublishDraftAsync(PublishDeliveryRequest request, CancellationToken ct);
    Task<DeliveryRecord?> GetDeliveryByTaskAsync(Guid taskId, Guid organizationId, CancellationToken ct);
}

public sealed class DeliveryService : IDeliveryService
{
    private readonly IMuniClawStore _store;
    private readonly IOrganizationAuthorizationService _auth;
    private readonly IGitProviderClient _gitClient;

    public DeliveryService(IMuniClawStore store, IOrganizationAuthorizationService auth, IGitProviderClient gitClient)
    {
        _store = store;
        _auth = auth;
        _gitClient = gitClient;
    }

    public async Task<DeliveryRecord> PublishDraftAsync(PublishDeliveryRequest request, CancellationToken ct)
    {
        if (!await _auth.CanAccessOrganizationAsync(request.UserId, request.OrganizationId, ct))
        {
            throw new UnauthorizedAccessException($"User {request.UserId} is not authorized for organization {request.OrganizationId}.");
        }

        if (!_store.Tasks.TryGetValue(request.TaskId, out var task) || task.OrganizationId != request.OrganizationId)
        {
            throw new KeyNotFoundException($"Task {request.TaskId} was not found.");
        }

        if (!_store.TaskRuns.TryGetValue(request.RunId, out var run) || run.TaskId != task.Id)
        {
            throw new KeyNotFoundException($"Run {request.RunId} was not found for task {request.TaskId}.");
        }

        if (!TaskRunStateTransitions.IsTerminal(run.Status) && run.Status != TaskRunStatus.Completed)
        {
            throw new InvalidOperationException($"Cannot publish delivery while run is still executing ({run.Status}).");
        }

        var project = _store.Projects[task.ProjectId];
        var repo = _store.RepositoryConnections[project.RepositoryConnectionId];

        // Ensure reviewed commit matches task's expected state
        if (string.IsNullOrWhiteSpace(request.ReviewedCommitSha))
        {
            throw new ArgumentException("Reviewed commit SHA must be provided.");
        }

        var gitRequest = new ReviewedPublicationRequest
        {
            OrganizationId = request.OrganizationId,
            TaskId = request.TaskId,
            RunId = request.RunId,
            RepositoryId = repo.RepositoryId,
            BaseBranch = task.BaseBranch,
            BaseCommitSha = request.BaseCommitSha,
            TargetTaskBranch = request.TargetBranch,
            ReviewedCommitSha = request.ReviewedCommitSha,
            PullRequestTitle = request.Title,
            PullRequestBody = request.Body
        };

        var result = await _gitClient.PublishDraftPullRequestAsync(gitRequest, ct);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Git publication failed: {result.ErrorMessage}");
        }

        // Idempotent delivery: update existing draft record if present, or create new
        var existingDelivery = _store.Deliveries.Values.FirstOrDefault(d => d.TaskId == request.TaskId);
        if (existingDelivery != null)
        {
            existingDelivery.ReviewedCommitSha = request.ReviewedCommitSha;
            existingDelivery.PublishedCommitSha = result.PublishedCommitSha;
            existingDelivery.RemotePrNumber = result.RemotePrNumber;
            existingDelivery.RemotePrUrl = result.RemotePrUrl;
            return existingDelivery;
        }

        var delivery = new DeliveryRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrganizationId,
            TaskId = request.TaskId,
            RunId = request.RunId,
            GitHost = repo.GitHost,
            RepositoryId = repo.RepositoryId,
            BaseCommitSha = request.BaseCommitSha,
            ReviewedCommitSha = request.ReviewedCommitSha,
            TargetBranch = request.TargetBranch,
            RemotePrNumber = result.RemotePrNumber,
            RemotePrUrl = result.RemotePrUrl,
            PublishedCommitSha = result.PublishedCommitSha,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _store.Deliveries[delivery.Id] = delivery;
        return delivery;
    }

    public Task<DeliveryRecord?> GetDeliveryByTaskAsync(Guid taskId, Guid organizationId, CancellationToken ct)
    {
        var record = _store.Deliveries.Values.FirstOrDefault(d => d.TaskId == taskId && d.OrganizationId == organizationId);
        return Task.FromResult(record);
    }
}
