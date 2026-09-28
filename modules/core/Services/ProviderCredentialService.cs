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

        // Only owner or admin can revoke
        var isAdmin = await _auth.IsOrganizationAdminAsync(userId, cred.OrganizationId, ct);
        if (cred.OwningUserId != userId && !isAdmin)
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
