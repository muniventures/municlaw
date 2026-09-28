using System.Collections.Concurrent;
using MuniClaw.Core.Contracts.Tasks;
using MuniClaw.Core.Contracts.Worker;
using MuniClaw.Worker.Http;

namespace MuniClaw.Worker.Supervisor;

public interface IWorkerEventBuffer
{
    long CurrentSequenceNumber { get; }
    int PendingCount { get; }
    void Enqueue(Guid taskId, Guid runId, TaskEventType eventType, string payloadJson, DateTimeOffset? timestamp = null);
    IReadOnlyList<TaskEventDto> GetPendingEvents(int maxBatch = 50);
    void Acknowledge(long sequenceNumber);
    Task<int> FlushAsync(IWorkerApiClient client, Guid workerId, Guid runId, long fencingToken, CancellationToken ct);
    void Reset();
}

public sealed class WorkerEventBuffer : IWorkerEventBuffer
{
    private readonly int _maxCapacity;
    private long _sequenceCounter = 0;
    private readonly List<TaskEventDto> _buffer = new();
    private readonly object _lock = new();

    public long CurrentSequenceNumber
    {
        get
        {
            lock (_lock) return _sequenceCounter;
        }
    }

    public int PendingCount
    {
        get
        {
            lock (_lock) return _buffer.Count;
        }
    }

    public WorkerEventBuffer(int maxCapacity = 1000)
    {
        _maxCapacity = maxCapacity;
    }

    public void Enqueue(Guid taskId, Guid runId, TaskEventType eventType, string payloadJson, DateTimeOffset? timestamp = null)
    {
        lock (_lock)
        {
            _sequenceCounter++;
            var isTruncated = false;

            if (_buffer.Count >= _maxCapacity)
            {
                // Drop oldest unacknowledged item to prevent unbounded memory growth
                _buffer.RemoveAt(0);
                isTruncated = true;
            }

            var dto = new TaskEventDto
            {
                EventId = Guid.NewGuid(),
                TaskId = taskId,
                RunId = runId,
                SequenceNumber = _sequenceCounter,
                EventType = eventType,
                Timestamp = timestamp ?? DateTimeOffset.UtcNow,
                PayloadJson = payloadJson,
                IsTruncated = isTruncated
            };

            _buffer.Add(dto);
        }
    }

    public IReadOnlyList<TaskEventDto> GetPendingEvents(int maxBatch = 50)
    {
        lock (_lock)
        {
            return _buffer.Take(maxBatch).ToList();
        }
    }

    public void Acknowledge(long sequenceNumber)
    {
        lock (_lock)
        {
            _buffer.RemoveAll(e => e.SequenceNumber <= sequenceNumber);
        }
    }

    public async Task<int> FlushAsync(IWorkerApiClient client, Guid workerId, Guid runId, long fencingToken, CancellationToken ct)
    {
        IReadOnlyList<TaskEventDto> batch;
        lock (_lock)
        {
            if (_buffer.Count == 0) return 0;
            batch = _buffer.ToList();
        }

        var upload = new WorkerEventBatchUpload
        {
            WorkerId = workerId,
            RunId = runId,
            FencingToken = fencingToken,
            Events = batch
        };

        var ack = await client.UploadEventsAsync(upload, ct);
        Acknowledge(ack.LastAcknowledgedSequenceNumber);

        return batch.Count;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _sequenceCounter = 0;
            _buffer.Clear();
        }
    }
}
