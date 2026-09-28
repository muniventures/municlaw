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

    void Clear();
}

public sealed class InMemoryMuniClawStore : IMuniClawStore
{
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
