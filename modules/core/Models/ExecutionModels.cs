using MuniClaw.Core.Contracts.Approvals;
using MuniClaw.Core.Contracts.Integrations;
using MuniClaw.Core.Contracts.Tasks;
using TaskStatus = MuniClaw.Core.Contracts.Tasks.TaskStatus;

namespace MuniClaw.Core.Models;

public sealed class TaskEntity
{
    public required Guid Id { get; set; }
    public required Guid OrganizationId { get; set; }
    public required Guid ProjectId { get; set; }
    public required string Title { get; set; }
    public TaskStatus Status { get; set; } = TaskStatus.Open;
    public required string TaskBranch { get; set; }
    public required string BaseBranch { get; set; }
    public string? BaseCommitSha { get; set; }
    public required Guid CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ArchivedAt { get; set; }

    public Project Project { get; set; } = null!;
    public ICollection<TaskRun> Runs { get; set; } = new List<TaskRun>();
}

public sealed partial class TaskRun
{
    public required Guid Id { get; set; }
    public required Guid TaskId { get; set; }
    public required Guid OrganizationId { get; set; }
    public int RunIndex { get; set; } = 1;
    public TaskRunStatus Status { get; set; } = TaskRunStatus.Queued;
    public required Guid ProviderCredentialReferenceId { get; set; }
    public required string ResolvedModel { get; set; }
    public required string Instruction { get; set; }
    public required string HarnessVersion { get; set; }
    public string? LeaseToken { get; set; }
    public long FencingToken { get; set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; set; }
    public decimal? MaxBudgetUsd { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? FailureReason { get; set; }

    public TaskEntity Task { get; set; } = null!;
    public ICollection<TaskEventRecord> Events { get; set; } = new List<TaskEventRecord>();
    public ICollection<ApprovalRequestRecord> Approvals { get; set; } = new List<ApprovalRequestRecord>();
}

public sealed class TaskEventRecord
{
    public required Guid Id { get; set; }
    public required Guid TaskId { get; set; }
    public required Guid RunId { get; set; }
    public required Guid OrganizationId { get; set; }
    public required long SequenceNumber { get; set; }
    public TaskEventType EventType { get; set; }
    public required string PayloadJson { get; set; }
    public bool IsTruncated { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public TaskRun TaskRun { get; set; } = null!;
}

public enum ApprovalRecordStatus
{
    Pending,
    Approved,
    Rejected,
    Expired
}

public sealed class ApprovalRequestRecord
{
    public required Guid Id { get; set; }
    public required Guid OrganizationId { get; set; }
    public required Guid TaskId { get; set; }
    public required Guid RunId { get; set; }
    public CapabilityCategory Capability { get; set; }
    public required string ActionDescription { get; set; }
    public required string ContentVersionHash { get; set; }
    public ApprovalRecordStatus Status { get; set; } = ApprovalRecordStatus.Pending;
    public Guid? DecidedByUserId { get; set; }
    public ApprovalDecisionType? Decision { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DecidedAt { get; set; }

    public TaskRun TaskRun { get; set; } = null!;
}

public sealed class DeliveryRecord
{
    public required Guid Id { get; set; }
    public required Guid OrganizationId { get; set; }
    public required Guid TaskId { get; set; }
    public required Guid RunId { get; set; }
    public GitHostType GitHost { get; set; }
    public required string RepositoryId { get; set; }
    public required string BaseCommitSha { get; set; }
    public required string ReviewedCommitSha { get; set; }
    public required string TargetBranch { get; set; }
    public string? RemotePrNumber { get; set; }
    public string? RemotePrUrl { get; set; }
    public string? PublishedCommitSha { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class UsageRecordEntity
{
    public required Guid Id { get; set; }
    public required Guid OrganizationId { get; set; }
    public required Guid TaskId { get; set; }
    public required Guid RunId { get; set; }
    public ModelProviderType Provider { get; set; }
    public required string ModelName { get; set; }
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
    public required string PriceProvenance { get; set; }
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}
