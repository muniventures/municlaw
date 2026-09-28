namespace MuniClaw.Worker.Sandbox;

public sealed record SandboxResourceLimits
{
    public double MaxCpuCores { get; init; } = 2.0;
    public long MaxMemoryBytes { get; init; } = 4L * 1024 * 1024 * 1024; // 4 GB
    public int MaxProcesses { get; init; } = 256;
    public long MaxStorageBytes { get; init; } = 10L * 1024 * 1024 * 1024; // 10 GB
    public TimeSpan ExecutionTimeout { get; init; } = TimeSpan.FromHours(1);

    public static SandboxResourceLimits Default => new();
}
