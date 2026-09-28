using System.Text.Json;
using MuniClaw.Core.Contracts.Tasks;
using MuniClaw.Core.Data;
using MuniClaw.Core.Models;

namespace MuniClaw.Core.Services;

public interface ITaskQueueService
{
    Task<int> GetActiveRunsCountAsync(Guid organizationId, CancellationToken ct = default);
    Task<int?> GetQueuePositionAsync(Guid organizationId, Guid runId, CancellationToken ct = default);
    Task<TaskRun?> PromoteNextQueuedTaskAsync(Guid organizationId, CancellationToken ct = default);
    Task<IReadOnlyList<TaskRun>> GetQueuedRunsAsync(Guid organizationId, CancellationToken ct = default);
}

public sealed class TaskQueueService : ITaskQueueService
{
    private readonly IMuniClawStore _store;
    private readonly object _lock = new();

    public TaskQueueService(IMuniClawStore store)
    {
        _store = store;
    }

    public static bool IsActiveExecuting(TaskRunStatus status) =>
        status is TaskRunStatus.Preparing
            or TaskRunStatus.Running
            or TaskRunStatus.AwaitingInput
            or TaskRunStatus.Cancelling;

    public Task<int> GetActiveRunsCountAsync(Guid organizationId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var count = _store.TaskRuns.Values
                .Count(r => r.OrganizationId == organizationId && IsActiveExecuting(r.Status));
            return Task.FromResult(count);
        }
    }

    public Task<int?> GetQueuePositionAsync(Guid organizationId, Guid runId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (!_store.TaskRuns.TryGetValue(runId, out var targetRun) ||
                targetRun.OrganizationId != organizationId ||
                targetRun.Status != TaskRunStatus.Queued)
            {
                return Task.FromResult<int?>(null);
            }

            var queuedRuns = _store.TaskRuns.Values
                .Where(r => r.OrganizationId == organizationId && r.Status == TaskRunStatus.Queued)
                .OrderBy(r => r.CreatedAt)
                .ToList();

            var index = queuedRuns.FindIndex(r => r.Id == runId);
            if (index >= 0)
            {
                var position = index + 1;
                targetRun.QueuePosition = position;
                return Task.FromResult<int?>(position);
            }

            return Task.FromResult<int?>(null);
        }
    }

    public Task<IReadOnlyList<TaskRun>> GetQueuedRunsAsync(Guid organizationId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var queuedRuns = _store.TaskRuns.Values
                .Where(r => r.OrganizationId == organizationId && r.Status == TaskRunStatus.Queued)
                .OrderBy(r => r.CreatedAt)
                .ToList();

            for (int i = 0; i < queuedRuns.Count; i++)
            {
                queuedRuns[i].QueuePosition = i + 1;
            }

            return Task.FromResult<IReadOnlyList<TaskRun>>(queuedRuns);
        }
    }

    public Task<TaskRun?> PromoteNextQueuedTaskAsync(Guid organizationId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var maxConcurrent = _store.Organizations.TryGetValue(organizationId, out var org)
                ? org.MaxConcurrentRuns
                : 2;

            var activeCount = _store.TaskRuns.Values
                .Count(r => r.OrganizationId == organizationId && IsActiveExecuting(r.Status));

            if (activeCount >= maxConcurrent)
            {
                return Task.FromResult<TaskRun?>(null);
            }

            var nextRun = _store.TaskRuns.Values
                .Where(r => r.OrganizationId == organizationId && r.Status == TaskRunStatus.Queued)
                .OrderBy(r => r.CreatedAt)
                .FirstOrDefault();

            if (nextRun == null)
            {
                return Task.FromResult<TaskRun?>(null);
            }

            nextRun.Status = TaskRunStatus.Preparing;
            nextRun.StartedAt = DateTimeOffset.UtcNow;
            nextRun.QueuePosition = null;

            var maxSeq = _store.TaskEvents.Values
                .Where(e => e.RunId == nextRun.Id)
                .Select(e => e.SequenceNumber)
                .DefaultIfEmpty(0L)
                .Max();

            var stepStartedEvent = new TaskEventRecord
            {
                Id = Guid.NewGuid(),
                TaskId = nextRun.TaskId,
                RunId = nextRun.Id,
                OrganizationId = organizationId,
                SequenceNumber = maxSeq + 1,
                EventType = TaskEventType.StepStarted,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    step = "preparing",
                    message = "Task promoted from queue to preparing"
                }),
                IsTruncated = false,
                CreatedAt = DateTimeOffset.UtcNow,
                TaskRun = nextRun
            };

            _store.TaskEvents[stepStartedEvent.Id] = stepStartedEvent;
            nextRun.Events.Add(stepStartedEvent);

            var remainingQueued = _store.TaskRuns.Values
                .Where(r => r.OrganizationId == organizationId && r.Status == TaskRunStatus.Queued)
                .OrderBy(r => r.CreatedAt)
                .ToList();

            for (int i = 0; i < remainingQueued.Count; i++)
            {
                remainingQueued[i].QueuePosition = i + 1;
            }

            return Task.FromResult<TaskRun?>(nextRun);
        }
    }
}
