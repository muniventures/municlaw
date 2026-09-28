using Microsoft.AspNetCore.Mvc;
using MuniClaw.Core.Contracts.Approvals;
using MuniClaw.Core.Services;

namespace MuniClaw.Api.Controllers;

[ApiController]
[Route("api/v1/organizations/{organizationId:guid}/approvals")]
public sealed class ApprovalsController : ControllerBase
{
    private readonly IApprovalService _approvals;

    public ApprovalsController(IApprovalService approvals)
    {
        _approvals = approvals;
    }

    [HttpGet]
    public async Task<IActionResult> ListPendingApprovals([FromRoute] Guid organizationId, CancellationToken ct)
    {
        var pending = await _approvals.ListPendingApprovalsAsync(organizationId, ct);
        return Ok(pending);
    }

    public sealed record DecisionPayload
    {
        public required Guid UserId { get; init; }
        public required ApprovalDecisionType Decision { get; init; }
        public required string ContentVersionHash { get; init; }
    }

    [HttpPost("{approvalId:guid}/decision")]
    public async Task<IActionResult> SubmitDecision(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid approvalId,
        [FromBody] DecisionPayload payload,
        CancellationToken ct)
    {
        try
        {
            var result = await _approvals.SubmitDecisionAsync(
                approvalId,
                payload.UserId,
                payload.Decision,
                payload.ContentVersionHash,
                ct);

            return Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "invalid_decision", message = ex.Message });
        }
    }
}
