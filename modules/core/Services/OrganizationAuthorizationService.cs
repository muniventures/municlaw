using MuniClaw.Core.Data;
using MuniClaw.Core.Models;

namespace MuniClaw.Core.Services;

public interface IOrganizationAuthorizationService
{
    Task<User> GetOrCreateUserAsync(string externalSubjectId, string email, CancellationToken ct);
    Task<bool> IsUserAllowlistedAsync(Guid userId, CancellationToken ct);
    Task SetUserAllowlistedAsync(Guid userId, bool isAllowlisted, CancellationToken ct);
    Task<OrganizationMembership> AddMemberToOrganizationAsync(Guid organizationId, Guid userId, MembershipRole role, CancellationToken ct);
    Task<bool> CanAccessOrganizationAsync(Guid userId, Guid organizationId, CancellationToken ct);
    Task<bool> IsOrganizationAdminAsync(Guid userId, Guid organizationId, CancellationToken ct);
}

public sealed class OrganizationAuthorizationService : IOrganizationAuthorizationService
{
    private readonly IMuniClawStore _store;
    private readonly object _lock = new();

    public OrganizationAuthorizationService(IMuniClawStore store)
    {
        _store = store;
    }

    public Task<User> GetOrCreateUserAsync(string externalSubjectId, string email, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalSubjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        var existing = _store.Users.Values.FirstOrDefault(u => u.ExternalSubjectId == externalSubjectId);
        if (existing != null)
        {
            return Task.FromResult(existing);
        }

        var newUser = new User
        {
            Id = Guid.NewGuid(),
            ExternalSubjectId = externalSubjectId,
            Email = email,
            IsAllowlisted = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        _store.Users[newUser.Id] = newUser;
        return Task.FromResult(newUser);
    }

    public Task<bool> IsUserAllowlistedAsync(Guid userId, CancellationToken ct)
    {
        if (_store.Users.TryGetValue(userId, out var user))
        {
            return Task.FromResult(user.IsAllowlisted);
        }
        return Task.FromResult(false);
    }

    public Task SetUserAllowlistedAsync(Guid userId, bool isAllowlisted, CancellationToken ct)
    {
        if (_store.Users.TryGetValue(userId, out var user))
        {
            user.IsAllowlisted = isAllowlisted;
        }
        else
        {
            throw new KeyNotFoundException($"User {userId} was not found.");
        }
        return Task.CompletedTask;
    }

    public Task<OrganizationMembership> AddMemberToOrganizationAsync(Guid organizationId, Guid userId, MembershipRole role, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_store.Organizations.ContainsKey(organizationId))
            {
                throw new KeyNotFoundException($"Organization {organizationId} was not found.");
            }

            if (!_store.Users.TryGetValue(userId, out var user))
            {
                throw new KeyNotFoundException($"User {userId} was not found.");
            }

            if (!user.IsAllowlisted)
            {
                throw new UnauthorizedAccessException($"User {userId} is not allowlisted for MuniClaw.");
            }

            // MVP Rule: Policy admits at most ONE active user membership per organization
            var activeMembers = _store.Memberships.Values
                .Where(m => m.OrganizationId == organizationId && m.IsActive)
                .ToList();

            if (activeMembers.Any(m => m.UserId != userId))
            {
                throw new InvalidOperationException(
                    $"Organization {organizationId} already has an active member. The MVP policy allows at most one active membership per organization.");
            }

            var existingMembership = activeMembers.FirstOrDefault(m => m.UserId == userId);
            if (existingMembership != null)
            {
                existingMembership.Role = role;
                return Task.FromResult(existingMembership);
            }

            var membership = new OrganizationMembership
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                UserId = userId,
                Role = role,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _store.Memberships[membership.Id] = membership;
            return Task.FromResult(membership);
        }
    }

    public Task<bool> CanAccessOrganizationAsync(Guid userId, Guid organizationId, CancellationToken ct)
    {
        if (!_store.Users.TryGetValue(userId, out var user) || !user.IsAllowlisted)
        {
            return Task.FromResult(false);
        }

        var hasActiveMembership = _store.Memberships.Values.Any(m =>
            m.OrganizationId == organizationId &&
            m.UserId == userId &&
            m.IsActive);

        return Task.FromResult(hasActiveMembership);
    }

    public Task<bool> IsOrganizationAdminAsync(Guid userId, Guid organizationId, CancellationToken ct)
    {
        if (!_store.Users.TryGetValue(userId, out var user) || !user.IsAllowlisted)
        {
            return Task.FromResult(false);
        }

        var isAdmin = _store.Memberships.Values.Any(m =>
            m.OrganizationId == organizationId &&
            m.UserId == userId &&
            m.IsActive &&
            m.Role == MembershipRole.Admin);

        return Task.FromResult(isAdmin);
    }
}
