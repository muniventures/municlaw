using Microsoft.AspNetCore.Mvc;
using MuniClaw.Core.Contracts.Preview;
using MuniClaw.Core.Data;
using MuniClaw.Core.Services;

namespace MuniClaw.Api.Controllers;

[ApiController]
[Route("api/v1/tasks/{taskId:guid}/preview")]
[Route("api/v1/organizations/{organizationId:guid}/tasks/{taskId:guid}/preview")]
public sealed class PreviewController : ControllerBase
{
    private readonly IPreviewDeploymentService _previewService;
    private readonly IMuniClawStore _store;

    public PreviewController(IPreviewDeploymentService previewService, IMuniClawStore store)
    {
        _previewService = previewService ?? throw new ArgumentNullException(nameof(previewService));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    [HttpPost]
    public async Task<IActionResult> TriggerPreview(
        [FromRoute] Guid taskId,
        [FromBody] TriggerPreviewRequest? request,
        [FromQuery] Guid? userId,
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        CancellationToken ct)
    {
        try
        {
            var actorUserId = ResolveActorUserId(taskId, userId, headerUserId);
            var deployment = await _previewService.TriggerPreviewAsync(taskId, actorUserId, ct);
            return Ok(PreviewDeploymentDto.FromModel(deployment));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = "task_not_found", message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "invalid_operation", message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetPreview(
        [FromRoute] Guid taskId,
        [FromQuery] Guid? userId,
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        CancellationToken ct)
    {
        try
        {
            var actorUserId = ResolveActorUserId(taskId, userId, headerUserId);
            var preview = await _previewService.GetPreviewAsync(taskId, actorUserId, ct);
            if (preview == null)
            {
                return NotFound(new { error = "preview_not_found", message = $"No preview deployment found for task {taskId}." });
            }

            return Ok(PreviewDeploymentDto.FromModel(preview));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = "task_not_found", message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
    }

    [HttpDelete]
    public async Task<IActionResult> TearDownPreview(
        [FromRoute] Guid taskId,
        [FromQuery] Guid? userId,
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        CancellationToken ct)
    {
        try
        {
            var actorUserId = ResolveActorUserId(taskId, userId, headerUserId);
            var preview = await _previewService.TearDownPreviewAsync(taskId, actorUserId, ct);
            return Ok(PreviewDeploymentDto.FromModel(preview));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = "not_found", message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "invalid_operation", message = ex.Message });
        }
    }

    private Guid ResolveActorUserId(Guid taskId, Guid? queryUserId, string? headerUserId)
    {
        if (queryUserId.HasValue && queryUserId.Value != Guid.Empty)
        {
            return queryUserId.Value;
        }

        if (!string.IsNullOrWhiteSpace(headerUserId) && Guid.TryParse(headerUserId, out var uid) && uid != Guid.Empty)
        {
            return uid;
        }

        if (_store.Tasks.TryGetValue(taskId, out var task))
        {
            if (task.CreatedByUserId != Guid.Empty)
            {
                return task.CreatedByUserId;
            }

            var member = _store.Memberships.Values
                .FirstOrDefault(m => m.OrganizationId == task.OrganizationId && m.IsActive);
            if (member != null)
            {
                return member.UserId;
            }
        }

        return Guid.Empty;
    }
}
