using System.Security.Cryptography;
using System.Text;
using MuniClaw.Core.Contracts.Integrations;
using MuniClaw.Core.Data;
using MuniClaw.Core.Models;

namespace MuniClaw.Core.Services;

public interface IWorkspaceProvisioningService
{
    Task<(CodingWorkspace Workspace, string RegistrationToken)> RequestWorkspaceAsync(Guid organizationId, Guid userId, string region, CancellationToken ct);
    Task<CodingWorkspace> CheckWorkspaceStatusAsync(Guid organizationId, Guid userId, CancellationToken ct);
    Task DeleteWorkspaceAsync(Guid organizationId, Guid userId, CancellationToken ct);
}

public sealed class WorkspaceProvisioningService : IWorkspaceProvisioningService
{
    private readonly IMuniClawStore _store;
    private readonly IOrganizationAuthorizationService _auth;
    private readonly IMinicloudInfrastructureClient _infraClient;

    public WorkspaceProvisioningService(
        IMuniClawStore store,
        IOrganizationAuthorizationService auth,
        IMinicloudInfrastructureClient infraClient)
    {
        _store = store;
        _auth = auth;
        _infraClient = infraClient;
    }

    public async Task<(CodingWorkspace Workspace, string RegistrationToken)> RequestWorkspaceAsync(Guid organizationId, Guid userId, string region, CancellationToken ct)
    {
        if (!await _auth.CanAccessOrganizationAsync(userId, organizationId, ct))
        {
            throw new UnauthorizedAccessException($"User {userId} is not authorized for organization {organizationId}.");
        }

        var existing = _store.Workspaces.Values.FirstOrDefault(w => w.OrganizationId == organizationId);
        if (existing != null && existing.Status != WorkspaceStatus.Failed && existing.Status != WorkspaceStatus.Terminated)
        {
            return (existing, string.Empty);
        }

        var rawToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));

        var workspace = new CodingWorkspace
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Status = WorkspaceStatus.Pending,
            SupervisorRegistrationTokenHash = tokenHash,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _store.Workspaces[workspace.Id] = workspace;

        var provisionRes = await _infraClient.ProvisionWorkspaceAsync(new WorkspaceProvisioningRequest
        {
            OrganizationId = organizationId,
            WorkspaceName = $"municlaw-org-{organizationId:N}",
            Region = region
        }, ct);

        workspace.MinicloudServerId = provisionRes.WorkspaceId.ToString();
        workspace.Status = provisionRes.Status switch
        {
            WorkspaceProvisioningStatus.Ready => WorkspaceStatus.Ready,
            WorkspaceProvisioningStatus.Failed => WorkspaceStatus.Failed,
            _ => WorkspaceStatus.Pending
        };
        workspace.VpsIpAddress = provisionRes.DedicatedVpsIp;
        workspace.FailureReason = provisionRes.FailureReason;

        return (workspace, rawToken);
    }

    public async Task<CodingWorkspace> CheckWorkspaceStatusAsync(Guid organizationId, Guid userId, CancellationToken ct)
    {
        if (!await _auth.CanAccessOrganizationAsync(userId, organizationId, ct))
        {
            throw new UnauthorizedAccessException($"User {userId} is not authorized for organization {organizationId}.");
        }

        var workspace = _store.Workspaces.Values.FirstOrDefault(w => w.OrganizationId == organizationId);
        if (workspace == null)
        {
            throw new KeyNotFoundException($"No workspace found for organization {organizationId}.");
        }

        if (workspace.Status == WorkspaceStatus.Pending && Guid.TryParse(workspace.MinicloudServerId, out var serverId))
        {
            var res = await _infraClient.GetWorkspaceStatusAsync(serverId, ct);
            workspace.Status = res.Status switch
            {
                WorkspaceProvisioningStatus.Ready => WorkspaceStatus.Ready,
                WorkspaceProvisioningStatus.Failed => WorkspaceStatus.Failed,
                _ => WorkspaceStatus.Pending
            };
            workspace.VpsIpAddress = res.DedicatedVpsIp;
            workspace.FailureReason = res.FailureReason;
            workspace.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return workspace;
    }

    public async Task DeleteWorkspaceAsync(Guid organizationId, Guid userId, CancellationToken ct)
    {
        if (!await _auth.IsOrganizationAdminAsync(userId, organizationId, ct))
        {
            throw new UnauthorizedAccessException("Only organization administrators can delete a workspace.");
        }

        var workspace = _store.Workspaces.Values.FirstOrDefault(w => w.OrganizationId == organizationId);
        if (workspace == null)
        {
            return;
        }

        if (Guid.TryParse(workspace.MinicloudServerId, out var serverId))
        {
            await _infraClient.DeleteWorkspaceAsync(serverId, ct);
        }

        workspace.Status = WorkspaceStatus.Terminated;
        workspace.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
