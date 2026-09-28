namespace MuniClaw.Core.Contracts.Tasks;

public enum TaskEventType
{
    StatusChanged,
    StepStarted,
    StepEnded,
    StepFailed,
    TextDelta,
    ReasoningDelta,
    ToolCalled,
    ToolSuccess,
    ToolFailed,
    DiffUpdated,
    CheckExecuted,
    ApprovalRequested,
    ApprovalResolved,
    Error
}

public sealed record TaskEventDto
{
    public required Guid EventId { get; init; }
    public required Guid TaskId { get; init; }
    public required Guid RunId { get; init; }
    public required long SequenceNumber { get; init; }
    public string Cursor => $"{RunId}:{SequenceNumber}";
    public required TaskEventType EventType { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string PayloadJson { get; init; }
    public bool IsTruncated { get; init; }

    public static (Guid RunId, long SequenceNumber)? ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        var parts = cursor.Split(':', 2);
        if (parts.Length == 2 && Guid.TryParse(parts[0], out var runId) && long.TryParse(parts[1], out var seq))
        {
            return (runId, seq);
        }

        return null;
    }
}
