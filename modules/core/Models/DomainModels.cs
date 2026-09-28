namespace MuniClaw.Core.Models;

public sealed class User
{
    public required Guid Id { get; set; }
    public required string ExternalSubjectId { get; set; }
    public required string Email { get; set; }
    public bool IsAllowlisted { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<OrganizationMembership> Memberships { get; set; } = new List<OrganizationMembership>();
}

public sealed class Organization
{
    public required Guid Id { get; set; }
    public required string Name { get; set; }
    public required string Slug { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<OrganizationMembership> Memberships { get; set; } = new List<OrganizationMembership>();
    public ICollection<Project> Projects { get; set; } = new List<Project>();
}

public enum MembershipRole
{
    Admin,
    Member
}

public sealed class OrganizationMembership
{
    public required Guid Id { get; set; }
    public required Guid OrganizationId { get; set; }
    public required Guid UserId { get; set; }
    public MembershipRole Role { get; set; } = MembershipRole.Member;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization Organization { get; set; } = null!;
    public User User { get; set; } = null!;
}

public enum WorkspaceStatus
{
    Pending,
    Ready,
    Failed,
    Terminated
}

public sealed class CodingWorkspace
{
    public required Guid Id { get; set; }
    public required Guid OrganizationId { get; set; }
    public string? MinicloudServerId { get; set; }
    public WorkspaceStatus Status { get; set; } = WorkspaceStatus.Pending;
    public string? VpsIpAddress { get; set; }
    public string? SupervisorRegistrationTokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? FailureReason { get; set; }
}

public enum GitHostType
{
    GitHub,
    GitLab
}

public sealed class RepositoryConnection
{
    public required Guid Id { get; set; }
    public required Guid OrganizationId { get; set; }
    public GitHostType GitHost { get; set; }
    public required string ExternalAccountId { get; set; }
    public string? InstallationId { get; set; }
    public required string RepositoryId { get; set; }
    public required string RepositoryFullName { get; set; }
    public required string DefaultBranch { get; set; }
    public required string SecretReferencePath { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Project
{
    public required Guid Id { get; set; }
    public required Guid OrganizationId { get; set; }
    public required Guid RepositoryConnectionId { get; set; }
    public required string Name { get; set; }
    public required string DefaultBaseBranch { get; set; }
    public string? SetupCommands { get; set; }
    public int SetupCommandsVersion { get; set; } = 1;
    public bool SetupCommandsConfirmed { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Organization Organization { get; set; } = null!;
    public RepositoryConnection RepositoryConnection { get; set; } = null!;
    public ICollection<TaskEntity> Tasks { get; set; } = new List<TaskEntity>();
}

public enum CredentialScope
{
    Personal,
    Organization
}

public sealed class ProviderCredentialReference
{
    public required Guid Id { get; set; }
    public required Guid OrganizationId { get; set; }
    public required Guid OwningUserId { get; set; }
    public required string ProviderName { get; set; }
    public required string Label { get; set; }
    public CredentialScope Scope { get; set; } = CredentialScope.Personal;
    public required string SecretReferencePath { get; set; }
    public bool IsRevoked { get; set; } = false;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
}
