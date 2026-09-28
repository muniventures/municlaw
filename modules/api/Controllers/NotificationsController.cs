using Microsoft.AspNetCore.Mvc;
using MuniClaw.Core.Contracts.Notifications;
using MuniClaw.Core.Data;
using MuniClaw.Core.Models;
using MuniClaw.Core.Services;

namespace MuniClaw.Api.Controllers;

[ApiController]
[Route("api/v1/notifications/channels")]
[Route("api/v1/organizations/{organizationId:guid}/notifications/channels")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly IMuniClawStore _store;

    public NotificationsController(IMuniClawStore store, INotificationService? notificationService = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _notificationService = notificationService ?? new NotificationService(store);
    }

    [HttpGet]
    public async Task<IActionResult> ListChannels(
        [FromRoute] Guid? organizationId,
        [FromQuery(Name = "organizationId")] Guid? queryOrgId,
        [FromQuery] Guid? orgId,
        [FromHeader(Name = "X-Organization-Id")] string? headerOrgId,
        CancellationToken ct)
    {
        var targetOrgId = ResolveOrganizationId(organizationId, queryOrgId, orgId, headerOrgId);
        var channels = await _notificationService.ListChannelsAsync(targetOrgId, ct);
        var dtos = channels.Select(NotificationChannelDto.FromModel).ToList();
        return Ok(dtos);
    }

    [HttpGet("{channelId:guid}")]
    public async Task<IActionResult> GetChannel(
        [FromRoute] Guid channelId,
        [FromRoute] Guid? organizationId,
        [FromQuery(Name = "organizationId")] Guid? queryOrgId,
        [FromQuery] Guid? orgId,
        [FromHeader(Name = "X-Organization-Id")] string? headerOrgId,
        CancellationToken ct)
    {
        var targetOrgId = ResolveOrganizationId(organizationId, queryOrgId, orgId, headerOrgId);
        var channel = await _notificationService.GetChannelAsync(targetOrgId, channelId, ct);
        if (channel == null)
        {
            return NotFound(new { error = "not_found", message = $"Notification channel {channelId} not found." });
        }
        return Ok(NotificationChannelDto.FromModel(channel));
    }

    [HttpPost]
    public async Task<IActionResult> CreateChannel(
        [FromRoute] Guid? organizationId,
        [FromQuery(Name = "organizationId")] Guid? queryOrgId,
        [FromQuery] Guid? orgId,
        [FromHeader(Name = "X-Organization-Id")] string? headerOrgId,
        [FromBody] CreateNotificationChannelRequest request,
        CancellationToken ct)
    {
        try
        {
            var targetOrgId = request.OrganizationId ?? ResolveOrganizationId(organizationId, queryOrgId, orgId, headerOrgId);
            if (targetOrgId == Guid.Empty)
            {
                var firstOrg = _store.Organizations.Keys.FirstOrDefault();
                if (firstOrg != Guid.Empty)
                {
                    targetOrgId = firstOrg;
                }
                else
                {
                    var newOrg = new Organization
                    {
                        Id = Guid.NewGuid(),
                        Name = "Default Organization",
                        Slug = "default-org"
                    };
                    _store.Organizations[newOrg.Id] = newOrg;
                    targetOrgId = newOrg.Id;
                }
            }

            var channel = await _notificationService.CreateChannelAsync(targetOrgId, request, ct);
            return Ok(NotificationChannelDto.FromModel(channel));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "invalid_argument", message = ex.Message });
        }
    }

    [HttpPut("{channelId:guid}")]
    public async Task<IActionResult> UpdateChannel(
        [FromRoute] Guid channelId,
        [FromRoute] Guid? organizationId,
        [FromQuery(Name = "organizationId")] Guid? queryOrgId,
        [FromQuery] Guid? orgId,
        [FromHeader(Name = "X-Organization-Id")] string? headerOrgId,
        [FromBody] UpdateNotificationChannelRequest request,
        CancellationToken ct)
    {
        try
        {
            var targetOrgId = ResolveOrganizationId(organizationId, queryOrgId, orgId, headerOrgId);
            var updated = await _notificationService.UpdateChannelAsync(targetOrgId, channelId, request, ct);
            return Ok(NotificationChannelDto.FromModel(updated));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = "not_found", message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "invalid_argument", message = ex.Message });
        }
    }

    [HttpDelete("{channelId:guid}")]
    public async Task<IActionResult> DeleteChannel(
        [FromRoute] Guid channelId,
        [FromRoute] Guid? organizationId,
        [FromQuery(Name = "organizationId")] Guid? queryOrgId,
        [FromQuery] Guid? orgId,
        [FromHeader(Name = "X-Organization-Id")] string? headerOrgId,
        CancellationToken ct)
    {
        var targetOrgId = ResolveOrganizationId(organizationId, queryOrgId, orgId, headerOrgId);
        var deleted = await _notificationService.DeleteChannelAsync(targetOrgId, channelId, ct);
        if (!deleted)
        {
            return NotFound(new { error = "not_found", message = $"Notification channel {channelId} not found." });
        }
        return Ok(new { status = "deleted", channelId });
    }

    [HttpPost("{channelId:guid}/test")]
    public async Task<IActionResult> SendTestPing(
        [FromRoute] Guid channelId,
        [FromRoute] Guid? organizationId,
        [FromQuery(Name = "organizationId")] Guid? queryOrgId,
        [FromQuery] Guid? orgId,
        [FromQuery] Guid? userId,
        [FromHeader(Name = "X-Organization-Id")] string? headerOrgId,
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        CancellationToken ct)
    {
        try
        {
            var targetOrgId = ResolveOrganizationId(organizationId, queryOrgId, orgId, headerOrgId);
            var actorUserId = userId ?? (Guid.TryParse(headerUserId, out var uid) ? uid : Guid.Empty);

            var success = await _notificationService.SendTestPingAsync(targetOrgId, channelId, actorUserId, ct);
            return Ok(new { success, channelId, status = success ? "Success" : "Failed" });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = "not_found", message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
    }

    private Guid ResolveOrganizationId(
        Guid? routeOrgId,
        Guid? queryOrgId1,
        Guid? queryOrgId2,
        string? headerOrgId)
    {
        if (routeOrgId.HasValue && routeOrgId.Value != Guid.Empty)
        {
            return routeOrgId.Value;
        }

        if (queryOrgId1.HasValue && queryOrgId1.Value != Guid.Empty)
        {
            return queryOrgId1.Value;
        }

        if (queryOrgId2.HasValue && queryOrgId2.Value != Guid.Empty)
        {
            return queryOrgId2.Value;
        }

        if (!string.IsNullOrWhiteSpace(headerOrgId) && Guid.TryParse(headerOrgId, out var headerGuid) && headerGuid != Guid.Empty)
        {
            return headerGuid;
        }

        var firstOrg = _store.Organizations.Keys.FirstOrDefault();
        if (firstOrg != Guid.Empty)
        {
            return firstOrg;
        }

        return Guid.Empty;
    }
}
