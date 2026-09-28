using MuniClaw.Core.Contracts.Integrations;
using MuniClaw.Core.Contracts.Tasks;
using MuniClaw.Core.Data;
using MuniClaw.Core.Models;

namespace MuniClaw.Core.Services;

public sealed record RegisterCredentialRequest
{
    public required Guid OrganizationId { get; init; }
    public required Guid UserId { get; init; }
    public required string ProviderName { get; init; }
    public required string Label { get; init; }
    public required CredentialScope Scope { get; init; }
    public required string ApiKey { get; init; }
}

public interface IProviderCredentialService
{
    Task<ProviderCredentialReference> RegisterCredentialAsync(RegisterCredentialRequest request, CancellationToken ct);
    Task RevokeCredentialAsync(Guid credentialId, Guid userId, CancellationToken ct);
    Task<IReadOnlyList<ProviderCredentialReference>> ListCredentialsAsync(Guid organizationId, Guid userId, CancellationToken ct);

    Task<ProviderCredentialReference> RegisterOrganizationCredentialAsync(
        RegisterOrganizationCredentialRequest request,
        string? actorUserId = null,
        CancellationToken ct = default);
    Task<ProviderCredentialReference> RegisterOrganizationCredentialAsync(
        RegisterOrganizationCredentialRequest request,
        Guid actorUserId,
        CancellationToken ct = default);

    Task<ProviderCredentialReference> UpdatePolicyAsync(
        string credentialId,
        OrganizationCredentialPolicy policy,
        string actorUserId,
        CancellationToken ct = default);
    Task<ProviderCredentialReference> UpdatePolicyAsync(
        Guid credentialId,
        OrganizationCredentialPolicy policy,
        Guid actorUserId,
        CancellationToken ct = default);

    Task<IReadOnlyList<ProviderCredentialReference>> GetOrganizationCredentialsAsync(
        string organizationId,
        string? actorUserId = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<ProviderCredentialReference>> GetOrganizationCredentialsAsync(
        Guid organizationId,
        Guid? actorUserId = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<ProviderCredentialReference>> GetAccessibleCredentialsAsync(
        string actorUserId,
        string organizationId,
        CancellationToken ct = default);
    Task<IReadOnlyList<ProviderCredentialReference>> GetAccessibleCredentialsAsync(
        Guid actorUserId,
        Guid organizationId,
        CancellationToken ct = default);

    Task<decimal> RecordSpendAsync(
        string organizationId,
        string credentialId,
        decimal amountUsd,
        CancellationToken ct = default);
    Task<decimal> RecordSpendAsync(
        Guid organizationId,
        Guid credentialId,
        decimal amountUsd,
        CancellationToken ct = default);
}

public sealed class ProviderCredentialService : IProviderCredentialService
{
    private readonly IMuniClawStore _store;
    private readonly IOrganizationAuthorizationService _auth;

    public ProviderCredentialService(IMuniClawStore store, IOrganizationAuthorizationService auth)
    {
        _store = store;
        _auth = auth;
    }

    public async Task<ProviderCredentialReference> RegisterCredentialAsync(RegisterCredentialRequest request, CancellationToken ct)
    {
        if (!await _auth.CanAccessOrganizationAsync(request.UserId, request.OrganizationId, ct))
        {
            throw new UnauthorizedAccessException($"User {request.UserId} is not authorized for organization {request.OrganizationId}.");
        }

        // MVP rule: Provider credentials are personal-only.
        if (request.Scope != CredentialScope.Personal)
        {
            throw new InvalidOperationException("MVP only supports Personal provider credentials.");
        }

        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            throw new ArgumentException("API key cannot be empty.");
        }

        var credId = Guid.NewGuid();
        // Secure scoped path in OpenBao
        var secretPath = $"secret/municlaw/orgs/{request.OrganizationId}/users/{request.UserId}/providers/{request.ProviderName}/{credId}";

        var reference = new ProviderCredentialReference
        {
            Id = credId,
            OrganizationId = request.OrganizationId,
            OwningUserId = request.UserId,
            ProviderName = request.ProviderName,
            Label = request.Label,
            Scope = CredentialScope.Personal,
            SecretReferencePath = secretPath,
            IsRevoked = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _store.ProviderCredentials[reference.Id] = reference;
        return reference;
    }

    public Task<ProviderCredentialReference> RegisterOrganizationCredentialAsync(
        RegisterOrganizationCredentialRequest request,
        string? actorUserId = null,
        CancellationToken ct = default)
    {
        var rawUser = !string.IsNullOrWhiteSpace(actorUserId) ? actorUserId : request.UserId;
        if (string.IsNullOrWhiteSpace(rawUser) || !Guid.TryParse(rawUser, out var userGuid))
        {
            throw new ArgumentException("Valid actor user ID is required to register an organization credential.");
        }

        return RegisterOrganizationCredentialAsync(request, userGuid, ct);
    }

    public async Task<ProviderCredentialReference> RegisterOrganizationCredentialAsync(
        RegisterOrganizationCredentialRequest request,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.OrganizationId) || !Guid.TryParse(request.OrganizationId, out var orgGuid))
        {
            throw new ArgumentException("Valid organization ID is required.");
        }

        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            throw new ArgumentException("API key cannot be empty.");
        }

        if (!await _auth.IsOrganizationAdminAsync(actorUserId, orgGuid, ct))
        {
            throw new UnauthorizedAccessException($"User {actorUserId} is not an administrator of organization {orgGuid}. Only organization administrators can register organization-scoped credentials.");
        }

        var credId = Guid.NewGuid();
        // Secure scoped path in OpenBao: municlaw/{orgId}/org-credentials/{credId}
        var secretPath = $"municlaw/{orgGuid}/org-credentials/{credId}";

        var policy = request.Policy ?? new OrganizationCredentialPolicy();
        if (policy.AllowedModels == null || policy.AllowedModels.Count == 0)
        {
            policy.AllowedModels = ["*"];
        }

        var reference = new ProviderCredentialReference
        {
            Id = credId,
            OrganizationId = orgGuid,
            OwningUserId = actorUserId,
            ProviderName = request.Provider,
            Label = request.Label,
            Scope = CredentialScope.Organization,
            Policy = policy,
            SecretReferencePath = secretPath,
            IsRevoked = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _store.StoreOrganizationCredential(reference);
        return reference;
    }

    public Task<ProviderCredentialReference> UpdatePolicyAsync(
        string credentialId,
        OrganizationCredentialPolicy policy,
        string actorUserId,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(credentialId, out var credGuid))
        {
            throw new ArgumentException("Invalid credential ID format.");
        }

        if (!Guid.TryParse(actorUserId, out var userGuid))
        {
            throw new ArgumentException("Invalid actor user ID format.");
        }

        return UpdatePolicyAsync(credGuid, policy, userGuid, ct);
    }

    public async Task<ProviderCredentialReference> UpdatePolicyAsync(
        Guid credentialId,
        OrganizationCredentialPolicy policy,
        Guid actorUserId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (!_store.ProviderCredentials.TryGetValue(credentialId, out var cred))
        {
            throw new KeyNotFoundException($"Credential {credentialId} was not found.");
        }

        if (cred.Scope != CredentialScope.Organization)
        {
            throw new InvalidOperationException($"Credential {credentialId} is not an organization credential.");
        }

        if (!await _auth.IsOrganizationAdminAsync(actorUserId, cred.OrganizationId, ct))
        {
            throw new UnauthorizedAccessException($"User {actorUserId} is not an administrator of organization {cred.OrganizationId}. Only organization administrators can update credential policies.");
        }

        if (policy.AllowedModels == null || policy.AllowedModels.Count == 0)
        {
            policy.AllowedModels = ["*"];
        }

        // Preserve current spend unless explicitly set non-zero in new policy
        if (policy.CurrentSpendUsd == 0m && cred.Policy != null && cred.Policy.CurrentSpendUsd > 0m)
        {
            policy.CurrentSpendUsd = cred.Policy.CurrentSpendUsd;
        }

        cred.Policy = policy;
        _store.UpdateCredentialPolicy(credentialId, policy);

        return cred;
    }

    public Task<IReadOnlyList<ProviderCredentialReference>> GetOrganizationCredentialsAsync(
        string organizationId,
        string? actorUserId = null,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(organizationId, out var orgGuid))
        {
            throw new ArgumentException("Invalid organization ID format.");
        }

        Guid? userGuid = !string.IsNullOrWhiteSpace(actorUserId) && Guid.TryParse(actorUserId, out var u) ? u : null;
        return GetOrganizationCredentialsAsync(orgGuid, userGuid, ct);
    }

    public async Task<IReadOnlyList<ProviderCredentialReference>> GetOrganizationCredentialsAsync(
        Guid organizationId,
        Guid? actorUserId = null,
        CancellationToken ct = default)
    {
        if (actorUserId.HasValue)
        {
            if (!await _auth.CanAccessOrganizationAsync(actorUserId.Value, organizationId, ct))
            {
                throw new UnauthorizedAccessException($"User {actorUserId.Value} is not authorized for organization {organizationId}.");
            }
        }

        return _store.GetOrganizationCredentials(organizationId);
    }

    public Task<IReadOnlyList<ProviderCredentialReference>> GetAccessibleCredentialsAsync(
        string actorUserId,
        string organizationId,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(actorUserId, out var userGuid))
        {
            throw new ArgumentException("Invalid actor user ID format.");
        }

        if (!Guid.TryParse(organizationId, out var orgGuid))
        {
            throw new ArgumentException("Invalid organization ID format.");
        }

        return GetAccessibleCredentialsAsync(userGuid, orgGuid, ct);
    }

    public async Task<IReadOnlyList<ProviderCredentialReference>> GetAccessibleCredentialsAsync(
        Guid actorUserId,
        Guid organizationId,
        CancellationToken ct = default)
    {
        if (!await _auth.CanAccessOrganizationAsync(actorUserId, organizationId, ct))
        {
            throw new UnauthorizedAccessException($"User {actorUserId} is not authorized for organization {organizationId}.");
        }

        var isAdmin = await _auth.IsOrganizationAdminAsync(actorUserId, organizationId, ct);

        var personalKeys = _store.ProviderCredentials.Values
            .Where(c => c.OrganizationId == organizationId &&
                        c.Scope == CredentialScope.Personal &&
                        c.OwningUserId == actorUserId &&
                        !c.IsRevoked);

        var orgKeys = _store.ProviderCredentials.Values
            .Where(c => c.OrganizationId == organizationId &&
                        c.Scope == CredentialScope.Organization &&
                        !c.IsRevoked &&
                        (isAdmin || c.Policy == null || !c.Policy.AdminOnly));

        var combined = personalKeys.Concat(orgKeys)
            .OrderByDescending(c => c.CreatedAt)
            .ToList();

        return combined;
    }

    public Task<decimal> RecordSpendAsync(
        string organizationId,
        string credentialId,
        decimal amountUsd,
        CancellationToken ct = default)
    {
        if (!Guid.TryParse(organizationId, out var orgGuid))
        {
            throw new ArgumentException("Invalid organization ID format.");
        }

        if (!Guid.TryParse(credentialId, out var credGuid))
        {
            throw new ArgumentException("Invalid credential ID format.");
        }

        return RecordSpendAsync(orgGuid, credGuid, amountUsd, ct);
    }

    public Task<decimal> RecordSpendAsync(
        Guid organizationId,
        Guid credentialId,
        decimal amountUsd,
        CancellationToken ct = default)
    {
        if (!_store.ProviderCredentials.TryGetValue(credentialId, out var cred) || cred.OrganizationId != organizationId)
        {
            throw new KeyNotFoundException($"Credential {credentialId} was not found in organization {organizationId}.");
        }

        if (cred.Scope != CredentialScope.Organization)
        {
            throw new InvalidOperationException($"Credential {credentialId} is not an organization credential.");
        }

        _store.IncrementCredentialSpend(organizationId, credentialId, amountUsd);
        return Task.FromResult(cred.Policy?.CurrentSpendUsd ?? 0m);
    }

    public async Task RevokeCredentialAsync(Guid credentialId, Guid userId, CancellationToken ct)
    {
        if (!_store.ProviderCredentials.TryGetValue(credentialId, out var cred))
        {
            throw new KeyNotFoundException($"Credential {credentialId} was not found.");
        }

        if (!await _auth.CanAccessOrganizationAsync(userId, cred.OrganizationId, ct))
        {
            throw new UnauthorizedAccessException($"User {userId} is not authorized for organization {cred.OrganizationId}.");
        }

        // Only owner or admin can revoke personal keys; only admin can revoke org keys
        var isAdmin = await _auth.IsOrganizationAdminAsync(userId, cred.OrganizationId, ct);
        if (cred.Scope == CredentialScope.Organization)
        {
            if (!isAdmin)
            {
                throw new UnauthorizedAccessException("Only an organization administrator can revoke an organization credential.");
            }
        }
        else if (cred.OwningUserId != userId && !isAdmin)
        {
            throw new UnauthorizedAccessException("Only the credential owner or an organization administrator can revoke this credential.");
        }

        cred.IsRevoked = true;
        cred.RevokedAt = DateTimeOffset.UtcNow;

        // Immediately cancel any active runs using this credential
        var affectedRuns = _store.TaskRuns.Values
            .Where(r => r.ProviderCredentialReferenceId == credentialId && !TaskRunStateTransitions.IsTerminal(r.Status))
            .ToList();

        foreach (var run in affectedRuns)
        {
            run.Status = TaskRunStatus.Failed;
            run.FailureReason = "Provider credential was revoked.";
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.LeaseToken = null;
        }
    }

    public async Task<IReadOnlyList<ProviderCredentialReference>> ListCredentialsAsync(Guid organizationId, Guid userId, CancellationToken ct)
    {
        if (!await _auth.CanAccessOrganizationAsync(userId, organizationId, ct))
        {
            throw new UnauthorizedAccessException($"User {userId} is not authorized for organization {organizationId}.");
        }

        var isAdmin = await _auth.IsOrganizationAdminAsync(userId, organizationId, ct);

        // Admins can see metadata of all personal keys in org; members only see their own
        var list = _store.ProviderCredentials.Values
            .Where(c => c.OrganizationId == organizationId && (isAdmin || c.OwningUserId == userId))
            .OrderByDescending(c => c.CreatedAt)
            .ToList();

        return list;
    }
}
