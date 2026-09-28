namespace MuniClaw.Core.Contracts.Integrations;

public enum ModelProviderType
{
    OpenAI,
    Anthropic,
    DeepSeek
}

public sealed record ModelUsageRecordDto
{
    public required Guid UsageRecordId { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid TaskId { get; init; }
    public required Guid RunId { get; init; }
    public required ModelProviderType Provider { get; init; }
    public required string ModelName { get; init; }
    public required long PromptTokens { get; init; }
    public required long CompletionTokens { get; init; }
    public long? TotalTokens => PromptTokens + CompletionTokens;
    public decimal? EstimatedCostUsd { get; init; }
    public required string PriceProvenance { get; init; }
    public required DateTimeOffset RecordedAt { get; init; }
}

public sealed record WorkspaceProvisioningRequest
{
    public required Guid OrganizationId { get; init; }
    public required string WorkspaceName { get; init; }
    public required string Region { get; init; }
}

public enum WorkspaceProvisioningStatus
{
    Pending,
    Ready,
    Failed
}

public sealed record WorkspaceProvisioningResponse
{
    public required Guid WorkspaceId { get; init; }
    public required Guid OrganizationId { get; init; }
    public required WorkspaceProvisioningStatus Status { get; init; }
    public string? DedicatedVpsIp { get; init; }
    public string? FailureReason { get; init; }
}

public interface IMinicloudInfrastructureClient
{
    Task<WorkspaceProvisioningResponse> ProvisionWorkspaceAsync(WorkspaceProvisioningRequest request, CancellationToken ct);
    Task<WorkspaceProvisioningResponse> GetWorkspaceStatusAsync(Guid workspaceId, CancellationToken ct);
    Task DeleteWorkspaceAsync(Guid workspaceId, CancellationToken ct);
}

public sealed record ReviewedPublicationRequest
{
    public required Guid OrganizationId { get; init; }
    public required Guid TaskId { get; init; }
    public required Guid RunId { get; init; }
    public required string RepositoryId { get; init; }
    public required string BaseBranch { get; init; }
    public required string BaseCommitSha { get; init; }
    public required string TargetTaskBranch { get; init; }
    public required string ReviewedCommitSha { get; init; }
    public required string PullRequestTitle { get; init; }
    public required string PullRequestBody { get; init; }
}

public sealed record ReviewedPublicationResult
{
    public required bool Success { get; init; }
    public string? RemotePrNumber { get; init; }
    public string? RemotePrUrl { get; init; }
    public string? PublishedCommitSha { get; init; }
    public string? ErrorMessage { get; init; }
}

public interface IGitProviderClient
{
    Task<ReviewedPublicationResult> PublishDraftPullRequestAsync(ReviewedPublicationRequest request, CancellationToken ct);
}
