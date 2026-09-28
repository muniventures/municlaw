using MuniClaw.Core.Contracts.Harness;

namespace MuniClaw.Worker.Harness;

public interface IOpenCodeSupervisor : IHarnessSupervisor
{
    string LoopbackHost { get; }
    int Port { get; }
}
