using MuniClaw.Core.Contracts.Tasks;

namespace MuniClaw.Core.Contracts.Harness;

public sealed record HarnessSessionInfo
{
    public required string SessionId { get; init; }
    public required string Slug { get; init; }
    public required string Version { get; init; }
    public required string Directory { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record HarnessPromptPart
{
    public required string Type { get; init; }
    public required string Text { get; init; }
}

public sealed record HarnessModelSpec
{
    public required string ProviderId { get; init; }
    public required string ModelId { get; init; }
}

public sealed record HarnessPrompt
{
    public required IReadOnlyList<HarnessPromptPart> Parts { get; init; }
    public required HarnessModelSpec Model { get; init; }
}

public sealed record HarnessDiffItem
{
    public required string Path { get; init; }
    public required string ChangeType { get; init; }
    public int Additions { get; init; }
    public int Deletions { get; init; }
}

public sealed record HarnessRawEvent
{
    public required string EventType { get; init; }
    public string? SessionId { get; init; }
    public required string PayloadJson { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public static class HarnessEventMapper
{
    public static TaskEventType? MapToCanonical(string rawOpenCodeEventType) => rawOpenCodeEventType switch
    {
        "EventSessionCreated" or "EventSessionUpdated" => TaskEventType.StatusChanged,
        "EventSessionNextStepStarted" => TaskEventType.StepStarted,
        "EventSessionNextStepEnded" => TaskEventType.StepEnded,
        "EventSessionNextStepFailed" => TaskEventType.StepFailed,
        "EventSessionNextTextDelta" => TaskEventType.TextDelta,
        "EventSessionNextReasoningDelta" => TaskEventType.ReasoningDelta,
        "EventSessionNextToolCalled" => TaskEventType.ToolCalled,
        "EventSessionNextToolSuccess" => TaskEventType.ToolSuccess,
        "EventSessionNextToolFailed" => TaskEventType.ToolFailed,
        "EventSessionDiff" => TaskEventType.DiffUpdated,
        "EventCommandExecuted" => TaskEventType.CheckExecuted,
        "EventPermissionAsked" or "EventPermissionV2Asked" => TaskEventType.ApprovalRequested,
        "EventPermissionReplied" or "EventPermissionV2Replied" => TaskEventType.ApprovalResolved,
        "EventSessionError" => TaskEventType.Error,
        _ => null // Internal UI or transient events not surfaced canonically
    };
}

public interface IHarnessAdapter
{
    Task<HarnessSessionInfo> CreateSessionAsync(string workingDirectory, CancellationToken ct);
    Task SendPromptAsync(string sessionId, HarnessPrompt prompt, CancellationToken ct);
    Task<IReadOnlyList<HarnessDiffItem>> GetDiffAsync(string sessionId, CancellationToken ct);
    Task<bool> AbortAsync(string sessionId, CancellationToken ct);
    IAsyncEnumerable<HarnessRawEvent> SubscribeEventsAsync(CancellationToken ct);
}
