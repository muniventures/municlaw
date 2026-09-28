using MuniClaw.Core.Contracts.Approvals;
using MuniClaw.Core.Contracts.Tasks;
using MuniClaw.Core.Contracts.Worker;
using MuniClaw.Core.Data;
using MuniClaw.Core.Models;

namespace MuniClaw.Core.Services;

public interface IApprovalService
{
    Task<ApprovalRequestRecord> CreateApprovalRequestAsync(Guid runId, CapabilityCategory capability, string description, string contentVersionHash, CancellationToken ct);
    Task<ApprovalRequestRecord> SubmitDecisionAsync(Guid approvalRequestId, Guid userId, ApprovalDecisionType decision, string contentVersionHash, CancellationToken ct);
    Task<IReadOnlyList<ApprovalRequestRecord>> ListPendingApprovalsAsync(Guid organizationId, CancellationToken ct);
}

public sealed class ApprovalService : IApprovalService
{
    private readonly IMuniClawStore _store;
    private readonly IOrganizationAuthorizationService _auth;
    private readonly IWorkerDispatchService _dispatch;

    public ApprovalService(IMuniClawStore store, IOrganizationAuthorizationService auth, IWorkerDispatchService dispatch)
    {
        _store = store;
        _auth = auth;
        _dispatch = dispatch;
    }

    public async Task<ApprovalRequestRecord> CreateApprovalRequestAsync(Guid runId, CapabilityCategory capability, string description, string contentVersionHash, CancellationToken ct)
    {
        if (!_store.TaskRuns.TryGetValue(runId, out var run))
        {
            throw new KeyNotFoundException($"Run {runId} was not found.");
        }

        // Automatic evaluation of PlatformDenied
        var policy = CapabilityPolicyEngine.Evaluate(capability, null);
        var initialStatus = policy == ApprovalPolicySetting.Deny
            ? ApprovalRecordStatus.Rejected
            : ApprovalRecordStatus.Pending;

        var record = new ApprovalRequestRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = run.OrganizationId,
            TaskId = run.TaskId,
            RunId = runId,
            Capability = capability,
            ActionDescription = description,
            ContentVersionHash = contentVersionHash,
            Status = initialStatus,
            CreatedAt = DateTimeOffset.UtcNow
        };

        if (initialStatus == ApprovalRecordStatus.Rejected)
        {
            record.Decision = ApprovalDecisionType.Deny;
            record.DecidedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            run.Status = TaskRunStatus.AwaitingInput;
        }

        _store.ApprovalRequests[record.Id] = record;
        await Task.CompletedTask;
        return record;
    }

    public async Task<ApprovalRequestRecord> SubmitDecisionAsync(Guid approvalRequestId, Guid userId, ApprovalDecisionType decision, string contentVersionHash, CancellationToken ct)
    {
        if (!_store.ApprovalRequests.TryGetValue(approvalRequestId, out var record))
        {
            throw new KeyNotFoundException($"Approval request {approvalRequestId} was not found.");
        }

        if (record.Status != ApprovalRecordStatus.Pending)
        {
            throw new InvalidOperationException($"Approval request {approvalRequestId} is already {record.Status}.");
        }

        // Recheck organization membership at decision time
        if (!await _auth.CanAccessOrganizationAsync(userId, record.OrganizationId, ct))
        {
            throw new UnauthorizedAccessException($"User {userId} is not authorized for organization {record.OrganizationId}.");
        }

        // Prevent stale approval: must match content version hash
        if (record.ContentVersionHash != contentVersionHash)
        {
            throw new InvalidOperationException("Approval content version mismatch. The underlying state or diff has changed since review.");
        }

        // PlatformDenied can NEVER be approved
        if (record.Capability == CapabilityCategory.PlatformDenied && decision == ApprovalDecisionType.AllowOnce)
        {
            throw new InvalidOperationException("PlatformDenied capabilities cannot be approved.");
        }

        record.Status = decision == ApprovalDecisionType.AllowOnce ? ApprovalRecordStatus.Approved : ApprovalRecordStatus.Rejected;
        record.Decision = decision;
        record.DecidedByUserId = userId;
        record.DecidedAt = DateTimeOffset.UtcNow;

        if (_store.TaskRuns.TryGetValue(record.RunId, out var run) && run.Status == TaskRunStatus.AwaitingInput)
        {
            run.Status = TaskRunStatus.Running;
        }

        // Queue command for worker
        await _dispatch.QueueCommandAsync(
            record.RunId,
            WorkerCommandType.Resume,
            $"{{\"approvalId\":\"{record.Id}\",\"decision\":\"{decision}\"}}",
            ct);

        return record;
    }

    public Task<IReadOnlyList<ApprovalRequestRecord>> ListPendingApprovalsAsync(Guid organizationId, CancellationToken ct)
    {
        var pending = _store.ApprovalRequests.Values
            .Where(a => a.OrganizationId == organizationId && a.Status == ApprovalRecordStatus.Pending)
            .OrderBy(a => a.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<ApprovalRequestRecord>>(pending);
    }
}
