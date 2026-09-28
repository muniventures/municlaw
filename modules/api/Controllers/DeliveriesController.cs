using Microsoft.AspNetCore.Mvc;
using MuniClaw.Core.Services;

namespace MuniClaw.Api.Controllers;

[ApiController]
[Route("api/v1/organizations/{organizationId:guid}/tasks/{taskId:guid}")]
public sealed class DeliveriesController : ControllerBase
{
    private readonly IDeliveryService _deliveryService;

    public DeliveriesController(IDeliveryService deliveryService)
    {
        _deliveryService = deliveryService;
    }

    public sealed record DeliverPayload
    {
        public required Guid RunId { get; init; }
        public required Guid UserId { get; init; }
        public required string BaseCommitSha { get; init; }
        public required string ReviewedCommitSha { get; init; }
        public required string TargetBranch { get; init; }
        public required string Title { get; init; }
        public required string Body { get; init; }
    }

    [HttpPost("deliver")]
    public async Task<IActionResult> PublishDelivery(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid taskId,
        [FromBody] DeliverPayload payload,
        CancellationToken ct)
    {
        try
        {
            var req = new PublishDeliveryRequest
            {
                OrganizationId = organizationId,
                TaskId = taskId,
                RunId = payload.RunId,
                UserId = payload.UserId,
                BaseCommitSha = payload.BaseCommitSha,
                ReviewedCommitSha = payload.ReviewedCommitSha,
                TargetBranch = payload.TargetBranch,
                Title = payload.Title,
                Body = payload.Body
            };

            var record = await _deliveryService.PublishDraftAsync(req, ct);
            return Ok(record);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "publication_failed", message = ex.Message });
        }
    }

    [HttpGet("delivery")]
    public async Task<IActionResult> GetDelivery(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid taskId,
        CancellationToken ct)
    {
        var record = await _deliveryService.GetDeliveryByTaskAsync(taskId, organizationId, ct);
        if (record == null)
        {
            return NotFound(new { error = "delivery_not_found" });
        }
        return Ok(record);
    }
}
