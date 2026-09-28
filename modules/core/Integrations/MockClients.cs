using MuniClaw.Core.Contracts.Integrations;

namespace MuniClaw.Core.Integrations;

public sealed class MockMinicloudInfrastructureClient : IMinicloudInfrastructureClient
{
    public Task<WorkspaceProvisioningResponse> ProvisionWorkspaceAsync(WorkspaceProvisioningRequest request, CancellationToken ct)
    {
        return Task.FromResult(new WorkspaceProvisioningResponse
        {
            WorkspaceId = Guid.NewGuid(),
            OrganizationId = request.OrganizationId,
            Status = WorkspaceProvisioningStatus.Ready,
            DedicatedVpsIp = "10.0.42.15"
        });
    }

    public Task<WorkspaceProvisioningResponse> GetWorkspaceStatusAsync(Guid workspaceId, CancellationToken ct)
    {
        return Task.FromResult(new WorkspaceProvisioningResponse
        {
            WorkspaceId = workspaceId,
            OrganizationId = Guid.Empty,
            Status = WorkspaceProvisioningStatus.Ready,
            DedicatedVpsIp = "10.0.42.15"
        });
    }

    public Task DeleteWorkspaceAsync(Guid workspaceId, CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}

public sealed class MockGitProviderClient : IGitProviderClient
{
    public Task<ReviewedPublicationResult> PublishDraftPullRequestAsync(ReviewedPublicationRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ReviewedCommitSha))
        {
            return Task.FromResult(new ReviewedPublicationResult
            {
                Success = false,
                ErrorMessage = "Reviewed commit SHA cannot be empty."
            });
        }

        var prNumber = "42";
        return Task.FromResult(new ReviewedPublicationResult
        {
            Success = true,
            RemotePrNumber = prNumber,
            RemotePrUrl = $"https://github.com/{request.RepositoryId}/pull/{prNumber}",
            PublishedCommitSha = request.ReviewedCommitSha
        });
    }
}
