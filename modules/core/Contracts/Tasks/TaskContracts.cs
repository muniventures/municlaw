using MuniClaw.Core.Models;

namespace MuniClaw.Core.Contracts.Tasks;

public sealed record CreateTaskRequest
{
    public required Guid OrganizationId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid UserId { get; init; }
    public required string Title { get; init; }
    public required string BaseBranch { get; init; }
    public string? BaseCommitSha { get; init; }
    public required Guid ProviderCredentialReferenceId { get; init; }
    public required string Model { get; init; }
    public required string Instruction { get; init; }
    public HarnessType HarnessType { get; init; } = HarnessType.OpenCode;
    public required string HarnessVersion { get; init; }
    public decimal? MaxBudgetUsd { get; init; }
    public string? IdempotencyKey { get; init; }
    public bool StrictConcurrencyLimit { get; init; } = false;
}
