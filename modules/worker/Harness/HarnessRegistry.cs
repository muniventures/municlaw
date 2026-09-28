using System.Collections.Concurrent;

namespace MuniClaw.Worker.Harness;

public sealed class HarnessRegistry : IHarnessRegistry
{
    private readonly ConcurrentDictionary<string, IHarnessSupervisor> _supervisors =
        new(StringComparer.OrdinalIgnoreCase);

    public HarnessRegistry()
    {
    }

    public HarnessRegistry(IEnumerable<IHarnessSupervisor>? supervisors)
    {
        if (supervisors != null)
        {
            foreach (var supervisor in supervisors)
            {
                Register(supervisor);
            }
        }
    }

    public void Register(IHarnessSupervisor supervisor)
    {
        ArgumentNullException.ThrowIfNull(supervisor);
        _supervisors[supervisor.HarnessType] = supervisor;
    }

    public IHarnessSupervisor GetSupervisor(string harnessType)
    {
        if (!string.IsNullOrWhiteSpace(harnessType) &&
            _supervisors.TryGetValue(harnessType, out var matched))
        {
            return matched;
        }

        // Defaults to OpenCode supervisor if unknown or empty
        if (_supervisors.TryGetValue("OpenCode", out var defaultOpenCode))
        {
            return defaultOpenCode;
        }

        if (!_supervisors.IsEmpty)
        {
            return _supervisors.Values.First();
        }

        throw new InvalidOperationException($"No harness supervisors are registered in {nameof(HarnessRegistry)}.");
    }

    public IReadOnlyList<IHarnessSupervisor> GetAllSupervisors()
    {
        return _supervisors.Values.ToList();
    }
}
