using MuniClaw.Core.Contracts.Harness;

namespace MuniClaw.Worker.Harness;

public interface IHarnessSupervisor
{
    string HarnessType { get; }
    string PinnedVersion { get; }
    bool IsRunning { get; }
    IHarnessAdapter Adapter { get; }

    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
