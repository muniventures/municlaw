namespace MuniClaw.Core.Contracts.Approvals;

public enum CapabilityCategory
{
    WorktreeReadWrite,
    LocalGit,
    SandboxedCommand,
    PackageNetwork,
    ExternalNetwork,
    DestructiveWorkspace,
    SetupConfig,
    RemoteGitWrite,
    DraftDelivery,
    PlatformDenied
}

public enum ApprovalPolicySetting
{
    Allow,
    Ask,
    Deny
}

public enum ApprovalDecisionType
{
    AllowOnce,
    Deny
}

public static class CapabilityPolicyEngine
{
    public static ApprovalPolicySetting GetDefaultSetting(CapabilityCategory category) => category switch
    {
        CapabilityCategory.WorktreeReadWrite => ApprovalPolicySetting.Allow,
        CapabilityCategory.LocalGit => ApprovalPolicySetting.Allow,
        CapabilityCategory.SandboxedCommand => ApprovalPolicySetting.Allow,
        CapabilityCategory.PackageNetwork => ApprovalPolicySetting.Allow,
        CapabilityCategory.ExternalNetwork => ApprovalPolicySetting.Ask,
        CapabilityCategory.DestructiveWorkspace => ApprovalPolicySetting.Ask,
        CapabilityCategory.SetupConfig => ApprovalPolicySetting.Ask,
        CapabilityCategory.RemoteGitWrite => ApprovalPolicySetting.Ask,
        CapabilityCategory.DraftDelivery => ApprovalPolicySetting.Ask,
        CapabilityCategory.PlatformDenied => ApprovalPolicySetting.Deny,
        _ => ApprovalPolicySetting.Deny
    };

    public static ApprovalPolicySetting Evaluate(CapabilityCategory category, ApprovalPolicySetting? userConfigured)
    {
        // PlatformDenied can never be overridden by user or repository configuration
        if (category == CapabilityCategory.PlatformDenied)
        {
            return ApprovalPolicySetting.Deny;
        }

        return userConfigured ?? GetDefaultSetting(category);
    }
}

public sealed record ApprovalRequestDto
{
    public required Guid ApprovalRequestId { get; init; }
    public required Guid OrganizationId { get; init; }
    public required Guid TaskId { get; init; }
    public required Guid RunId { get; init; }
    public required CapabilityCategory Capability { get; init; }
    public required string ActionDescription { get; init; }
    public required string ContentVersionHash { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? ExpiredAt { get; init; }
}

public sealed record ApprovalDecisionDto
{
    public required Guid ApprovalRequestId { get; init; }
    public required Guid DecidedByUserId { get; init; }
    public required ApprovalDecisionType Decision { get; init; }
    public required string ValidatedContentVersionHash { get; init; }
    public required DateTimeOffset DecidedAt { get; init; }
}
