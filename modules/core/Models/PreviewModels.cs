namespace MuniClaw.Core.Models;

public enum PreviewDeploymentStatus
{
    None,
    Deploying,
    Active,
    Failed,
    TornDown
}

public sealed class PreviewDeployment
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public Guid RunId { get; set; }
    public Guid OrganizationId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string NormalizedBranch { get; set; } = string.Empty;
    public string? CommitSha { get; set; }
    public string? PreviewUrl { get; set; }
    public PreviewDeploymentStatus Status { get; set; } = PreviewDeploymentStatus.None;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeployedAt { get; set; }
    public DateTimeOffset? TornDownAt { get; set; }
    public string? ErrorMessage { get; set; }
}
