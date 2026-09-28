using MuniClaw.Core.Models;

namespace MuniClaw.Core.Contracts.Preview;

public sealed record TriggerPreviewRequest(string? CustomSubdomain = null);

public sealed record PreviewDeploymentDto(
    Guid Id,
    Guid TaskId,
    Guid RunId,
    string BranchName,
    string NormalizedBranch,
    string? CommitSha,
    string? PreviewUrl,
    PreviewDeploymentStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DeployedAt,
    string? ErrorMessage)
{
    public static PreviewDeploymentDto FromModel(PreviewDeployment model)
    {
        return new PreviewDeploymentDto(
            model.Id,
            model.TaskId,
            model.RunId,
            model.BranchName,
            model.NormalizedBranch,
            model.CommitSha,
            model.PreviewUrl,
            model.Status,
            model.CreatedAt,
            model.DeployedAt,
            model.ErrorMessage);
    }
}
