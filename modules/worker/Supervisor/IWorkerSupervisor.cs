namespace MuniClaw.Worker.Supervisor;

public interface IWorkerSupervisor
{
    bool IsActive { get; }
    Guid? CurrentRunId { get; }
    string? CurrentLeaseToken { get; }
    long CurrentFencingToken { get; }
    long LastCommandCursor { get; }

    Task<bool> PollAndProcessOnceAsync(CancellationToken ct);
}
