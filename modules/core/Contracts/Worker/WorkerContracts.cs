using MuniClaw.Core.Contracts.Tasks;

namespace MuniClaw.Core.Contracts.Worker;

public sealed record WorkerClaimRequest
{
    public required Guid WorkerId { get; init; }
    public required Guid OrganizationId { get; init; }
    public required string SupportedHarnessVersion { get; init; }
}

public sealed record WorkerClaimResponse
{
    public required bool HasWork { get; init; }
    public Guid? RunId { get; init; }
    public Guid? TaskId { get; init; }
    public string? RepositoryCloneUrl { get; init; }
    public string? TaskBranch { get; init; }
    public string? BaseCommit { get; init; }
    public string? HarnessVersion { get; init; }
    public string? LeaseToken { get; init; }
    public long FencingToken { get; init; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; init; }
}

public enum WorkerCommandType
{
    Abort,
    Cancel,
    Resume,
    Reconcile
}

public sealed record WorkerCommand
{
    public required long CommandSequence { get; init; }
    public required WorkerCommandType CommandType { get; init; }
    public required string PayloadJson { get; init; }
}

public sealed record WorkerHeartbeatRequest
{
    public required Guid WorkerId { get; init; }
    public required Guid RunId { get; init; }
    public required long FencingToken { get; init; }
    public required string CurrentLeaseToken { get; init; }
    public required long LastAcknowledgedCommandCursor { get; init; }
}

public sealed record WorkerHeartbeatResponse
{
    public required bool IsLeaseValid { get; init; }
    public string? ExtendedLeaseToken { get; init; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; init; }
    public required IReadOnlyList<WorkerCommand> PendingCommands { get; init; }
}

public sealed record WorkerEventBatchUpload
{
    public required Guid WorkerId { get; init; }
    public required Guid RunId { get; init; }
    public required long FencingToken { get; init; }
    public required IReadOnlyList<TaskEventDto> Events { get; init; }
}

public sealed record WorkerEventBatchAck
{
    public required Guid RunId { get; init; }
    public required long LastAcknowledgedSequenceNumber { get; init; }
}
