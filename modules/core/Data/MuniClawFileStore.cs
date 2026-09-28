using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using MuniClaw.Core.Models;

namespace MuniClaw.Core.Data;

public sealed class MuniClawFileStore : IMuniClawStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _dataDirectoryPath;
    private readonly object _ioLock = new();
    private readonly object _credentialLock = new();

    public string DataDirectoryPath => _dataDirectoryPath;

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

    public MuniClawFileStore(string dataDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(dataDirectoryPath))
        {
            throw new ArgumentException("Data directory path cannot be null or empty.", nameof(dataDirectoryPath));
        }

        _dataDirectoryPath = Path.GetFullPath(dataDirectoryPath);
        Directory.CreateDirectory(_dataDirectoryPath);
        LoadSnapshot();
    }

    public void SaveSnapshot()
    {
        lock (_ioLock)
        {
            Directory.CreateDirectory(_dataDirectoryPath);
            var targetFile = Path.Combine(_dataDirectoryPath, "snapshot.json");
            var tempFile = Path.Combine(_dataDirectoryPath, $"snapshot.{Guid.NewGuid():N}.tmp");

            var snapshot = new SnapshotData
            {
                Users = Users.Values.ToList(),
                Organizations = Organizations.Values.ToList(),
                Memberships = Memberships.Values.ToList(),
                Workspaces = Workspaces.Values.ToList(),
                RepositoryConnections = RepositoryConnections.Values.ToList(),
                Projects = Projects.Values.ToList(),
                ProviderCredentials = ProviderCredentials.Values.ToList(),
                Tasks = Tasks.Values.ToList(),
                TaskRuns = TaskRuns.Values.ToList(),
                TaskEvents = TaskEvents.Values.ToList(),
                ApprovalRequests = ApprovalRequests.Values.ToList(),
                Deliveries = Deliveries.Values.ToList(),
                UsageRecords = UsageRecords.Values.ToList()
            };

            var json = JsonSerializer.Serialize(snapshot, JsonOptions);
            try
            {
                File.WriteAllText(tempFile, json);
                File.Move(tempFile, targetFile, overwrite: true);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch
                    {
                        // best-effort cleanup
                    }
                }
            }
        }
    }

    public void LoadSnapshot()
    {
        lock (_ioLock)
        {
            var snapshotFile = GetSnapshotFilePath();
            if (!File.Exists(snapshotFile))
            {
                return;
            }

            var json = File.ReadAllText(snapshotFile);
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            var data = JsonSerializer.Deserialize<SnapshotData>(json, JsonOptions);
            if (data == null)
            {
                return;
            }

            Users.Clear();
            foreach (var u in data.Users) Users[u.Id] = u;

            Organizations.Clear();
            foreach (var o in data.Organizations) Organizations[o.Id] = o;

            Memberships.Clear();
            foreach (var m in data.Memberships) Memberships[m.Id] = m;

            Workspaces.Clear();
            foreach (var w in data.Workspaces) Workspaces[w.Id] = w;

            RepositoryConnections.Clear();
            foreach (var r in data.RepositoryConnections) RepositoryConnections[r.Id] = r;

            Projects.Clear();
            foreach (var p in data.Projects) Projects[p.Id] = p;

            ProviderCredentials.Clear();
            foreach (var c in data.ProviderCredentials) ProviderCredentials[c.Id] = c;

            Tasks.Clear();
            foreach (var t in data.Tasks) Tasks[t.Id] = t;

            TaskRuns.Clear();
            foreach (var tr in data.TaskRuns) TaskRuns[tr.Id] = tr;

            TaskEvents.Clear();
            foreach (var e in data.TaskEvents) TaskEvents[e.Id] = e;

            ApprovalRequests.Clear();
            foreach (var a in data.ApprovalRequests) ApprovalRequests[a.Id] = a;

            Deliveries.Clear();
            foreach (var d in data.Deliveries) Deliveries[d.Id] = d;

            UsageRecords.Clear();
            foreach (var ur in data.UsageRecords) UsageRecords[ur.Id] = ur;
        }
    }

    private string GetSnapshotFilePath()
    {
        var primary = Path.Combine(_dataDirectoryPath, "snapshot.json");
        if (File.Exists(primary))
        {
            return primary;
        }

        var secondary = Path.Combine(_dataDirectoryPath, "municlaw-store.json");
        if (File.Exists(secondary))
        {
            return secondary;
        }

        return primary;
    }

    public void StoreOrganizationCredential(ProviderCredentialReference credential)
    {
        lock (_credentialLock)
        {
            ProviderCredentials[credential.Id] = credential;
            SaveSnapshot();
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
                SaveSnapshot();
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
                cred.Policy ??= new OrganizationCredentialPolicy();
                cred.Policy.CurrentSpendUsd += amountUsd;
                SaveSnapshot();
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
        lock (_credentialLock)
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
            SaveSnapshot();
        }
    }

    public sealed class SnapshotData
    {
        public List<User> Users { get; set; } = [];
        public List<Organization> Organizations { get; set; } = [];
        public List<OrganizationMembership> Memberships { get; set; } = [];
        public List<CodingWorkspace> Workspaces { get; set; } = [];
        public List<RepositoryConnection> RepositoryConnections { get; set; } = [];
        public List<Project> Projects { get; set; } = [];
        public List<ProviderCredentialReference> ProviderCredentials { get; set; } = [];
        public List<TaskEntity> Tasks { get; set; } = [];
        public List<TaskRun> TaskRuns { get; set; } = [];
        public List<TaskEventRecord> TaskEvents { get; set; } = [];
        public List<ApprovalRequestRecord> ApprovalRequests { get; set; } = [];
        public List<DeliveryRecord> Deliveries { get; set; } = [];
        public List<UsageRecordEntity> UsageRecords { get; set; } = [];
    }
}
