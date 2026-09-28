using Microsoft.AspNetCore.Mvc;
using MuniClaw.Core.Services;

namespace MuniClaw.Api.Controllers;

[ApiController]
[Route("api/v1/organizations/{organizationId:guid}/workspace")]
public sealed class WorkspacesController : ControllerBase
{
    private readonly IWorkspaceProvisioningService _workspaces;

    public WorkspacesController(IWorkspaceProvisioningService workspaces)
    {
        _workspaces = workspaces;
    }

    public sealed record ProvisionWorkspacePayload
    {
        public required Guid UserId { get; init; }
        public string Region { get; init; } = "us-east-1";
    }

    [HttpPost]
    public async Task<IActionResult> RequestWorkspace(
        [FromRoute] Guid organizationId,
        [FromBody] ProvisionWorkspacePayload payload,
        CancellationToken ct)
    {
        try
        {
            var (workspace, registrationToken) = await _workspaces.RequestWorkspaceAsync(
                organizationId,
                payload.UserId,
                payload.Region,
                ct);

            return Ok(new
            {
                workspace.Id,
                workspace.OrganizationId,
                workspace.Status,
                workspace.VpsIpAddress,
                workspace.CreatedAt,
                RegistrationToken = string.IsNullOrEmpty(registrationToken) ? null : registrationToken
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetWorkspaceStatus(
        [FromRoute] Guid organizationId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        try
        {
            var workspace = await _workspaces.CheckWorkspaceStatusAsync(organizationId, userId, ct);
            return Ok(new
            {
                workspace.Id,
                workspace.OrganizationId,
                workspace.Status,
                workspace.VpsIpAddress,
                workspace.FailureReason,
                workspace.CreatedAt,
                workspace.UpdatedAt
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { error = "workspace_not_found" });
        }
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteWorkspace(
        [FromRoute] Guid organizationId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        try
        {
            await _workspaces.DeleteWorkspaceAsync(organizationId, userId, ct);
            return Ok(new { status = "deleted", organizationId });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
    }
}
