using Microsoft.AspNetCore.Mvc;
using MuniClaw.Core.Contracts.Worker;
using MuniClaw.Core.Services;

namespace MuniClaw.Api.Controllers;

[ApiController]
[Route("api/v1/worker")]
public sealed class WorkerController : ControllerBase
{
    private readonly IWorkerDispatchService _dispatch;

    public WorkerController(IWorkerDispatchService dispatch)
    {
        _dispatch = dispatch;
    }

    [HttpPost("claim")]
    public async Task<IActionResult> ClaimWork([FromBody] WorkerClaimRequest request, CancellationToken ct)
    {
        var response = await _dispatch.ClaimWorkAsync(request, ct);
        return Ok(response);
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] WorkerHeartbeatRequest request, CancellationToken ct)
    {
        var response = await _dispatch.HeartbeatAsync(request, ct);
        if (!response.IsLeaseValid)
        {
            return StatusCode(410, new { error = "lease_expired", message = "Fencing token or lease token is invalid." });
        }
        return Ok(response);
    }

    [HttpPost("events")]
    public async Task<IActionResult> UploadEvents([FromBody] WorkerEventBatchUpload upload, CancellationToken ct)
    {
        try
        {
            var ack = await _dispatch.UploadEventsAsync(upload, ct);
            return Ok(ack);
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(409, new { error = "fencing_conflict", message = ex.Message });
        }
    }
}
