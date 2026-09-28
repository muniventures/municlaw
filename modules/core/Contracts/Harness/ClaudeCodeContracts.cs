using System.Text.Json;
using MuniClaw.Core.Contracts.Tasks;

namespace MuniClaw.Core.Contracts.Harness;

public static class ClaudeCodeEventMapper
{
    public static TaskEventType? MapToCanonical(string? rawClaudeCodeEventType)
    {
        if (string.IsNullOrWhiteSpace(rawClaudeCodeEventType))
        {
            return null;
        }

        var normalized = rawClaudeCodeEventType.Trim().ToLowerInvariant();
        return normalized switch
        {
            "text_delta" or "assistant_response" => TaskEventType.TextDelta,
            "thought_delta" or "reasoning" => TaskEventType.ReasoningDelta,
            "tool_call" or "tool_use" => TaskEventType.ToolCalled,
            "tool_result" => TaskEventType.ToolSuccess,
            "tool_error" => TaskEventType.ToolFailed,
            "diff" => TaskEventType.DiffUpdated,
            "ask_user" => TaskEventType.ApprovalRequested,
            "error" => TaskEventType.Error,
            _ => Enum.TryParse<TaskEventType>(rawClaudeCodeEventType, true, out var canonical) ? canonical : null
        };
    }

    public static TaskEventType? MapToCanonical(string? rawClaudeCodeEventType, bool isSuccess)
    {
        if (string.IsNullOrWhiteSpace(rawClaudeCodeEventType))
        {
            return null;
        }

        var normalized = rawClaudeCodeEventType.Trim().ToLowerInvariant();
        if (normalized == "tool_result")
        {
            return isSuccess ? TaskEventType.ToolSuccess : TaskEventType.ToolFailed;
        }

        return MapToCanonical(rawClaudeCodeEventType);
    }
}

public sealed record ClaudeCodeEventPayload
{
    public string? Type { get; init; }
    public string? Content { get; init; }
    public string? Tool { get; init; }
    public string? Input { get; init; }
    public string? Output { get; init; }
    public bool? Success { get; init; }
    public string? Error { get; init; }
}
