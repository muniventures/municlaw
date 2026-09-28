using System.Collections.Concurrent;
using MuniClaw.Core.Contracts.Tasks;
using MuniClaw.Core.Contracts.Worker;
using MuniClaw.Core.Data;
using MuniClaw.Core.Models;

namespace MuniClaw.Core.Services;

public interface IWorkerDispatchService
{
    Task<WorkerClaimResponse> ClaimWorkAsync(WorkerClaimRequest request, CancellationToken ct);
    Task<WorkerHeartbeatResponse> HeartbeatAsync(WorkerHeartbeatRequest request, CancellationToken ct);
    Task<WorkerEventBatchAck> UploadEventsAsync(WorkerEventBatchUpload upload, CancellationToken ct);
    Task QueueCommandAsync(Guid runId, WorkerCommandType type, string payloadJson, CancellationToken ct);
    Task<IReadOnlyList<TaskEventDto>> GetEventsSinceAsync(Guid runId, long sequenceNumber, CancellationToken ct);
    Task ReconcileExpiredLeasesAsync(CancellationToken ct);
}

public sealed class WorkerDispatchService : IWorkerDispatchService
{
    private readonly IMuniClawStore _store;
    private readonly ConcurrentDictionary<Guid, List<WorkerCommand>> _runCommands = new();
    private readonly ConcurrentDictionary<Guid, long> _fencingCounters = new();
    private readonly object _claimLock = new();

    public WorkerDispatchService(IMuniClawStore store)
    {
        _store = store;
    }

    public Task<WorkerClaimResponse> ClaimWorkAsync(WorkerClaimRequest request, CancellationToken ct)
    {
        lock (_claimLock)
        {
            // Enforce concurrency limit: MaxConcurrentRuns per organization (default 1)
            var maxConcurrent = 1;
            if (_store.Organizations.TryGetValue(request.OrganizationId, out var org))
            {
                maxConcurrent = org.MaxConcurrentRuns;
            }

            var activeCount = _store.TaskRuns.Values.Count(r =>
                r.OrganizationId == request.OrganizationId &&
                (r.Status == TaskRunStatus.Preparing ||
                 r.Status == TaskRunStatus.Running ||
                 r.Status == TaskRunStatus.AwaitingInput ||
                 r.Status == TaskRunStatus.Cancelling));

            if (activeCount >= maxConcurrent)
            {
                return Task.FromResult(new WorkerClaimResponse
                {
                    HasWork = false,
                    FencingToken = 0
                });
            }

            var nextRun = _store.TaskRuns.Values
                .Where(r => r.OrganizationId == request.OrganizationId && r.Status == TaskRunStatus.Queued)
                .OrderBy(r => r.CreatedAt)
                .ThenBy(r => r.RunIndex)
                .FirstOrDefault();

            if (nextRun == null)
            {
                return Task.FromResult(new WorkerClaimResponse
                {
                    HasWork = false,
                    FencingToken = 0
                });
            }

            var task = _store.Tasks[nextRun.TaskId];
            var project = _store.Projects[task.ProjectId];
            var repoConn = _store.RepositoryConnections[project.RepositoryConnectionId];

            var fencingToken = _fencingCounters.AddOrUpdate(nextRun.Id, 1, (_, current) => current + 1);
            var leaseToken = Guid.NewGuid().ToString("N");
            var leaseExpiry = DateTimeOffset.UtcNow.AddSeconds(30);

            nextRun.Status = TaskRunStatus.Preparing;
            nextRun.LeaseToken = leaseToken;
            nextRun.FencingToken = fencingToken;
            nextRun.LeaseExpiresAtUtc = leaseExpiry;
            nextRun.StartedAt = DateTimeOffset.UtcNow;
            nextRun.QueuePosition = null;

            var remainingQueued = _store.TaskRuns.Values
                .Where(r => r.OrganizationId == request.OrganizationId && r.Status == TaskRunStatus.Queued)
                .OrderBy(r => r.CreatedAt)
                .ToList();

            for (int i = 0; i < remainingQueued.Count; i++)
            {
                remainingQueued[i].QueuePosition = i + 1;
            }

            return Task.FromResult(new WorkerClaimResponse
            {
                HasWork = true,
                RunId = nextRun.Id,
                TaskId = task.Id,
                RepositoryCloneUrl = $"https://git.mock/{repoConn.RepositoryFullName}.git",
                TaskBranch = task.TaskBranch,
                BaseCommit = task.BaseCommitSha,
                HarnessType = nextRun.HarnessType,
                HarnessVersion = nextRun.HarnessVersion,
                LeaseToken = leaseToken,
                FencingToken = fencingToken,
                LeaseExpiresAtUtc = leaseExpiry
            });
        }
    }

    public Task<WorkerHeartbeatResponse> HeartbeatAsync(WorkerHeartbeatRequest request, CancellationToken ct)
    {
        if (!_store.TaskRuns.TryGetValue(request.RunId, out var run))
        {
            return Task.FromResult(new WorkerHeartbeatResponse
            {
                IsLeaseValid = false,
                PendingCommands = Array.Empty<WorkerCommand>()
            });
        }

        // Fencing check: reject stale/expired worker
        if (run.FencingToken != request.FencingToken || run.LeaseToken != request.CurrentLeaseToken)
        {
            return Task.FromResult(new WorkerHeartbeatResponse
            {
                IsLeaseValid = false,
                PendingCommands = Array.Empty<WorkerCommand>()
            });
        }

        // Renew lease for 30s
        var newLeaseToken = Guid.NewGuid().ToString("N");
        var newExpiry = DateTimeOffset.UtcNow.AddSeconds(30);
        run.LeaseToken = newLeaseToken;
        run.LeaseExpiresAtUtc = newExpiry;

        // Retrieve pending commands past LastAcknowledgedCommandCursor
        var pending = Array.Empty<WorkerCommand>();
        if (_runCommands.TryGetValue(request.RunId, out var commands))
        {
            lock (commands)
            {
                pending = commands
                    .Where(c => c.CommandSequence > request.LastAcknowledgedCommandCursor)
                    .ToArray();
            }
        }

        return Task.FromResult(new WorkerHeartbeatResponse
        {
            IsLeaseValid = true,
            ExtendedLeaseToken = newLeaseToken,
            LeaseExpiresAtUtc = newExpiry,
            PendingCommands = pending
        });
    }

    public Task<WorkerEventBatchAck> UploadEventsAsync(WorkerEventBatchUpload upload, CancellationToken ct)
    {
        if (!_store.TaskRuns.TryGetValue(upload.RunId, out var run))
        {
            throw new KeyNotFoundException($"Run {upload.RunId} was not found.");
        }

        // Strict fencing check
        if (run.FencingToken != upload.FencingToken)
        {
            throw new InvalidOperationException($"Fencing token {upload.FencingToken} is obsolete. Current fencing token is {run.FencingToken}.");
        }

        long lastAck = 0;
        foreach (var evt in upload.Events)
        {
            var record = new TaskEventRecord
            {
                Id = evt.EventId == Guid.Empty ? Guid.NewGuid() : evt.EventId,
                TaskId = upload.RunId,
                RunId = upload.RunId,
                OrganizationId = run.OrganizationId,
                SequenceNumber = evt.SequenceNumber,
                EventType = evt.EventType,
                PayloadJson = evt.PayloadJson,
                IsTruncated = evt.IsTruncated,
                CreatedAt = evt.Timestamp
            };

            _store.TaskEvents[record.Id] = record;
            lastAck = Math.Max(lastAck, evt.SequenceNumber);

            // Synchronize TaskRun status transitions if matching event
            if (evt.EventType == TaskEventType.StatusChanged || evt.EventType == TaskEventType.StepStarted)
            {
                if (run.Status == TaskRunStatus.Preparing)
                {
                    run.Status = TaskRunStatus.Running;
                }
            }
            else if (evt.EventType == TaskEventType.ApprovalRequested)
            {
                run.Status = TaskRunStatus.AwaitingInput;
            }
        }

        return Task.FromResult(new WorkerEventBatchAck
        {
            RunId = upload.RunId,
            LastAcknowledgedSequenceNumber = lastAck
        });
    }

    public Task QueueCommandAsync(Guid runId, WorkerCommandType type, string payloadJson, CancellationToken ct)
    {
        var commandList = _runCommands.GetOrAdd(runId, _ => new List<WorkerCommand>());
        lock (commandList)
        {
            var nextSeq = commandList.Count + 1;
            commandList.Add(new WorkerCommand
            {
                CommandSequence = nextSeq,
                CommandType = type,
                PayloadJson = payloadJson
            });
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TaskEventDto>> GetEventsSinceAsync(Guid runId, long sequenceNumber, CancellationToken ct)
    {
        var events = _store.TaskEvents.Values
            .Where(e => e.RunId == runId && e.SequenceNumber > sequenceNumber)
            .OrderBy(e => e.SequenceNumber)
            .Select(e => new TaskEventDto
            {
                EventId = e.Id,
                TaskId = e.TaskId,
                RunId = e.RunId,
                SequenceNumber = e.SequenceNumber,
                EventType = e.EventType,
                Timestamp = e.CreatedAt,
                PayloadJson = e.PayloadJson,
                IsTruncated = e.IsTruncated
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<TaskEventDto>>(events);
    }

    public Task ReconcileExpiredLeasesAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var activeRuns = _store.TaskRuns.Values
            .Where(r => (r.Status == TaskRunStatus.Preparing || r.Status == TaskRunStatus.Running) &&
                        r.LeaseExpiresAtUtc.HasValue && r.LeaseExpiresAtUtc.Value < now)
            .ToList();

        foreach (var run in activeRuns)
        {
            run.Status = TaskRunStatus.Failed;
            run.FailureReason = "Worker heartbeat expired. Execution marked offline.";
            run.CompletedAt = now;
            // Invalidate lease
            run.LeaseToken = null;
        }

        return Task.CompletedTask;
    }
}
