using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MuniClaw.Core.Contracts.Tasks;
using MuniClaw.Core.Data;
using MuniClaw.Core.Models;
using MuniClaw.Core.Services;

namespace MuniClaw.Api.Controllers;

[ApiController]
[Route("api/v1/organizations/{organizationId:guid}/tasks")]
public sealed class TasksController : ControllerBase
{
    private readonly ITaskLifecycleService _lifecycle;
    private readonly IWorkerDispatchService _dispatch;
    private readonly ITaskQueueService _queueService;
    private readonly IMuniClawStore _store;

    public TasksController(
        ITaskLifecycleService lifecycle,
        IWorkerDispatchService dispatch,
        ITaskQueueService queueService,
        IMuniClawStore store)
    {
        _lifecycle = lifecycle;
        _dispatch = dispatch;
        _queueService = queueService;
        _store = store;
    }

    [HttpGet]
    public async Task<IActionResult> ListTasks([FromRoute] Guid organizationId, CancellationToken ct)
    {
        var tasks = await _lifecycle.ListTasksAsync(organizationId, ct);
        return Ok(tasks);
    }

    [HttpGet("{taskId:guid}")]
    public async Task<IActionResult> GetTask([FromRoute] Guid organizationId, [FromRoute] Guid taskId, CancellationToken ct)
    {
        var task = await _lifecycle.GetTaskAsync(taskId, organizationId, ct);
        if (task == null)
        {
            return NotFound(new { error = "task_not_found", message = $"Task {taskId} was not found." });
        }
        return Ok(task);
    }

    [HttpGet("/api/v1/tasks/{taskId:guid}/queue")]
    [HttpGet("{taskId:guid}/queue")]
    public async Task<IActionResult> GetTaskQueue(
        [FromRoute] Guid taskId,
        [FromRoute] Guid? organizationId,
        CancellationToken ct)
    {
        TaskEntity? task = null;
        if (organizationId.HasValue)
        {
            task = await _lifecycle.GetTaskAsync(taskId, organizationId.Value, ct);
        }
        else if (_store.Tasks.TryGetValue(taskId, out var foundTask))
        {
            task = foundTask;
        }

        if (task == null)
        {
            return NotFound(new { error = "task_not_found", message = $"Task {taskId} was not found." });
        }

        var orgId = task.OrganizationId;
        var maxConcurrentRuns = _store.Organizations.TryGetValue(orgId, out var org)
            ? org.MaxConcurrentRuns
            : 2;

        var activeRuns = await _queueService.GetActiveRunsCountAsync(orgId, ct);

        var latestRun = _store.TaskRuns.Values
            .Where(r => r.TaskId == taskId)
            .OrderByDescending(r => r.RunIndex)
            .FirstOrDefault();

        int? queuePosition = null;
        if (latestRun != null && latestRun.Status == TaskRunStatus.Queued)
        {
            queuePosition = await _queueService.GetQueuePositionAsync(orgId, latestRun.Id, ct);
        }

        return Ok(new
        {
            queuePosition,
            activeRuns,
            maxConcurrentRuns
        });
    }

    public sealed record CreateTaskPayload
    {
        public required Guid ProjectId { get; init; }
        public required Guid UserId { get; init; }
        public required string Title { get; init; }
        public required string BaseBranch { get; init; }
        public string? BaseCommitSha { get; init; }
        public required Guid ProviderCredentialReferenceId { get; init; }
        public required string Model { get; init; }
        public required string Instruction { get; init; }
        public required string HarnessVersion { get; init; }
        public decimal? MaxBudgetUsd { get; init; }
        public string? IdempotencyKey { get; init; }
    }

    [HttpPost]
    public async Task<IActionResult> CreateTask(
        [FromRoute] Guid organizationId,
        [FromBody] CreateTaskPayload payload,
        CancellationToken ct)
    {
        try
        {
            var req = new CreateTaskRequest
            {
                OrganizationId = organizationId,
                ProjectId = payload.ProjectId,
                UserId = payload.UserId,
                Title = payload.Title,
                BaseBranch = payload.BaseBranch,
                BaseCommitSha = payload.BaseCommitSha,
                ProviderCredentialReferenceId = payload.ProviderCredentialReferenceId,
                Model = payload.Model,
                Instruction = payload.Instruction,
                HarnessVersion = payload.HarnessVersion,
                MaxBudgetUsd = payload.MaxBudgetUsd,
                IdempotencyKey = payload.IdempotencyKey
            };

            var (task, run) = await _lifecycle.CreateTaskAsync(req, ct);
            return CreatedAtAction(nameof(GetTask), new { organizationId, taskId = task.Id }, new
            {
                task,
                initialRun = run
            });
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

    public sealed record FollowUpPayload
    {
        public required Guid UserId { get; init; }
        public required string Instruction { get; init; }
    }

    [HttpPost("{taskId:guid}/followup")]
    public async Task<IActionResult> FollowUp(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid taskId,
        [FromBody] FollowUpPayload payload,
        CancellationToken ct)
    {
        try
        {
            var run = await _lifecycle.CreateFollowUpRunAsync(taskId, payload.UserId, payload.Instruction, ct);
            return Ok(run);
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

    public sealed record CancelPayload
    {
        public required Guid UserId { get; init; }
        public string? Reason { get; init; }
    }

    [HttpPost("{taskId:guid}/runs/{runId:guid}/cancel")]
    public async Task<IActionResult> CancelRun(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid taskId,
        [FromRoute] Guid runId,
        [FromBody] CancelPayload payload,
        CancellationToken ct)
    {
        try
        {
            await _lifecycle.CancelRunAsync(runId, payload.UserId, payload.Reason ?? "Cancelled by user", ct);
            return Ok(new { status = "cancelling", runId });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
    }

    [HttpGet("{taskId:guid}/runs/{runId:guid}/events")]
    public async Task StreamEvents(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid taskId,
        [FromRoute] Guid runId,
        [FromQuery] long sinceSequence = 0,
        CancellationToken ct = default)
    {
        // Support Last-Event-ID header cursor format {runId}:{seq}
        if (Request.Headers.TryGetValue("Last-Event-ID", out var lastEventIdHeader))
        {
            var parsed = TaskEventDto.ParseCursor(lastEventIdHeader.ToString());
            if (parsed.HasValue && parsed.Value.RunId == runId)
            {
                sinceSequence = Math.Max(sinceSequence, parsed.Value.SequenceNumber);
            }
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.Append("Connection", "keep-alive");

        var currentSeq = sinceSequence;
        var events = await _dispatch.GetEventsSinceAsync(runId, currentSeq, ct);

        foreach (var evt in events)
        {
            var sse = $"id: {evt.Cursor}\nevent: {evt.EventType}\ndata: {evt.PayloadJson}\n\n";
            await Response.WriteAsync(sse, ct);
            await Response.Body.FlushAsync(ct);
            currentSeq = Math.Max(currentSeq, evt.SequenceNumber);
        }
    }
}
