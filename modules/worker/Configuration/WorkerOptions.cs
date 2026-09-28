namespace MuniClaw.Worker.Configuration;

public sealed record WorkerOptions
{
    public Guid WorkerId { get; init; } = Guid.NewGuid();
    public required Guid OrganizationId { get; init; }
    public Uri ServerBaseUri { get; init; } = new("http://localhost:5000");
    public TimeSpan ClaimPollInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromSeconds(5);
    public string SupportedHarnessVersion { get; init; } = "1.18.32";
    public string TasksRootPath { get; init; } = "/tmp/municlaw/tasks";
    public string OpenCodeHost { get; init; } = "127.0.0.1";
    public int OpenCodePort { get; init; } = 4096;
    public string OpenCodePassword { get; init; } = "opencode-vps-secret";
    public string OpenCodeExecutablePath { get; init; } = "opencode";
    public int MaxBufferedEvents { get; init; } = 1000;
    public int RetentionDays { get; init; } = 7;
    public long MinFreeDiskSpaceBytes { get; init; } = 1024L * 1024 * 1024; // 1 GB
}
