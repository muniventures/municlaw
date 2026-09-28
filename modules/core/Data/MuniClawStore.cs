using System.Collections.Concurrent;
using MuniClaw.Core.Models;

namespace MuniClaw.Core.Data;

public interface IMuniClawStore
{
    ConcurrentDictionary<Guid, User> Users { get; }
    ConcurrentDictionary<Guid, Organization> Organizations { get; }
    ConcurrentDictionary<Guid, OrganizationMembership> Memberships { get; }
    ConcurrentDictionary<Guid, CodingWorkspace> Workspaces { get; }
    ConcurrentDictionary<Guid, RepositoryConnection> RepositoryConnections { get; }
    ConcurrentDictionary<Guid, Project> Projects { get; }
    ConcurrentDictionary<Guid, ProviderCredentialReference> ProviderCredentials { get; }
    ConcurrentDictionary<Guid, TaskEntity> Tasks { get; }
    ConcurrentDictionary<Guid, TaskRun> TaskRuns { get; }
    ConcurrentDictionary<Guid, TaskEventRecord> TaskEvents { get; }
    ConcurrentDictionary<Guid, ApprovalRequestRecord> ApprovalRequests { get; }
    ConcurrentDictionary<Guid, DeliveryRecord> Deliveries { get; }
    ConcurrentDictionary<Guid, UsageRecordEntity> UsageRecords { get; }

    void StoreOrganizationCredential(ProviderCredentialReference credential);
    IReadOnlyList<ProviderCredentialReference> GetOrganizationCredentials(Guid organizationId);
    IReadOnlyList<ProviderCredentialReference> GetOrganizationCredentials(string organizationId);
    bool UpdateCredentialPolicy(Guid credentialId, OrganizationCredentialPolicy policy);
    bool IncrementCredentialSpend(Guid organizationId, Guid credentialId, decimal amountUsd);
    bool IncrementCredentialSpend(string organizationId, string credentialId, decimal amountUsd);

    void Clear();
}

public sealed class InMemoryMuniClawStore : IMuniClawStore
{
    private readonly object _credentialLock = new();

    public ConcurrentDictionary<Guid, User> Users { get; } = new();
    public ConcurrentDictionary<Guid, Organization> Organizations { get; } = new();
    public ConcurrentDictionary<Guid, OrganizationMembership> Memberships { get; } = new();
    public ConcurrentDictionary<Guid, CodingWorkspace> Workspaces { get; } = new();
    public ConcurrentDictionary<Guid, RepositoryConnection> RepositoryConnections { get; } = new();
    public ConcurrentDictionary<Guid, Project> Projects { get; } = new();
    public ConcurrentDictionary<Guid, ProviderCredentialReference> ProviderCredentials { get; } = new();
    public ConcurrentDictionary<Guid, TaskEntity> Tasks { get; } = new();
    public ConcurrentDictionary<Guid, TaskRun> TaskRuns { get; } = new();
    public ConcurrentDictionary<Guid, TaskEventRecord> TaskEvents { get; } = new();
    public ConcurrentDictionary<Guid, ApprovalRequestRecord> ApprovalRequests { get; } = new();
    public ConcurrentDictionary<Guid, DeliveryRecord> Deliveries { get; } = new();
    public ConcurrentDictionary<Guid, UsageRecordEntity> UsageRecords { get; } = new();

    public void StoreOrganizationCredential(ProviderCredentialReference credential)
    {
        lock (_credentialLock)
        {
            ProviderCredentials[credential.Id] = credential;
        }
    }

    public IReadOnlyList<ProviderCredentialReference> GetOrganizationCredentials(Guid organizationId)
    {
        lock (_credentialLock)
        {
            return ProviderCredentials.Values
                .Where(c => c.OrganizationId == organizationId && c.Scope == CredentialScope.Organization && !c.IsRevoked)
                .OrderByDescending(c => c.CreatedAt)
                .ToList();
        }
    }

    public IReadOnlyList<ProviderCredentialReference> GetOrganizationCredentials(string organizationId)
    {
        if (Guid.TryParse(organizationId, out var orgGuid))
        {
            return GetOrganizationCredentials(orgGuid);
        }
        return Array.Empty<ProviderCredentialReference>();
    }

    public bool UpdateCredentialPolicy(Guid credentialId, OrganizationCredentialPolicy policy)
    {
        lock (_credentialLock)
        {
            if (ProviderCredentials.TryGetValue(credentialId, out var cred) && cred.Scope == CredentialScope.Organization)
            {
                cred.Policy = policy;
                return true;
            }
            return false;
        }
    }

    public bool IncrementCredentialSpend(Guid organizationId, Guid credentialId, decimal amountUsd)
    {
        lock (_credentialLock)
        {
            if (ProviderCredentials.TryGetValue(credentialId, out var cred) &&
                cred.OrganizationId == organizationId &&
                cred.Scope == CredentialScope.Organization)
            {
                if (cred.Policy == null)
                {
                    cred.Policy = new OrganizationCredentialPolicy();
                }
                cred.Policy.CurrentSpendUsd += amountUsd;
                return true;
            }
            return false;
        }
    }

    public bool IncrementCredentialSpend(string organizationId, string credentialId, decimal amountUsd)
    {
        if (Guid.TryParse(organizationId, out var orgGuid) && Guid.TryParse(credentialId, out var credGuid))
        {
            return IncrementCredentialSpend(orgGuid, credGuid, amountUsd);
        }
        return false;
    }

    public void Clear()
    {
        Users.Clear();
        Organizations.Clear();
        Memberships.Clear();
        Workspaces.Clear();
        RepositoryConnections.Clear();
        Projects.Clear();
        ProviderCredentials.Clear();
        Tasks.Clear();
        TaskRuns.Clear();
        TaskEvents.Clear();
        ApprovalRequests.Clear();
        Deliveries.Clear();
        UsageRecords.Clear();
    }
}

