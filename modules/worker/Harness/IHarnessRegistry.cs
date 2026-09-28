namespace MuniClaw.Worker.Harness;

public interface IHarnessRegistry
{
    void Register(IHarnessSupervisor supervisor);
    IHarnessSupervisor GetSupervisor(string harnessType);
    IReadOnlyList<IHarnessSupervisor> GetAllSupervisors();
}
