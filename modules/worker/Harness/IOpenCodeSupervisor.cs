using MuniClaw.Core.Contracts.Harness;

namespace MuniClaw.Worker.Harness;

public interface IOpenCodeSupervisor
{
    string PinnedVersion { get; }
    string LoopbackHost { get; }
    int Port { get; }
    bool IsRunning { get; }
    IHarnessAdapter Adapter { get; }

    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
