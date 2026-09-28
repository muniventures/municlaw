using Microsoft.AspNetCore.Mvc;
using MuniClaw.Core.Contracts.Integrations;
using MuniClaw.Core.Models;
using MuniClaw.Core.Services;

namespace MuniClaw.Api.Controllers;

[ApiController]
[Route("api/v1/organizations/{organizationId:guid}/credentials")]
public sealed class CredentialsController : ControllerBase
{
    private readonly IProviderCredentialService _credentials;

    public CredentialsController(IProviderCredentialService credentials)
    {
        _credentials = credentials;
    }

    [HttpGet]
    public async Task<IActionResult> ListCredentials(
        [FromRoute] Guid organizationId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        try
        {
            var list = await _credentials.ListCredentialsAsync(organizationId, userId, ct);
            // Redact internal OpenBao paths and ensure raw key is never returned
            var safeDtos = list.Select(c => new
            {
                c.Id,
                c.OrganizationId,
                c.OwningUserId,
                c.ProviderName,
                c.Label,
                c.Scope,
                c.Policy,
                c.IsRevoked,
                c.CreatedAt,
                c.RevokedAt
            });
            return Ok(safeDtos);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
    }

    public sealed record RegisterKeyPayload
    {
        public required Guid UserId { get; init; }
        public required string ProviderName { get; init; }
        public required string Label { get; init; }
        public required CredentialScope Scope { get; init; }
        public required string ApiKey { get; init; }
    }

    [HttpPost]
    public async Task<IActionResult> RegisterCredential(
        [FromRoute] Guid organizationId,
        [FromBody] RegisterKeyPayload payload,
        CancellationToken ct)
    {
        try
        {
            var req = new RegisterCredentialRequest
            {
                OrganizationId = organizationId,
                UserId = payload.UserId,
                ProviderName = payload.ProviderName,
                Label = payload.Label,
                Scope = payload.Scope,
                ApiKey = payload.ApiKey
            };

            var cred = await _credentials.RegisterCredentialAsync(req, ct);
            return Ok(new
            {
                cred.Id,
                cred.OrganizationId,
                cred.OwningUserId,
                cred.ProviderName,
                cred.Label,
                cred.Scope,
                cred.Policy,
                cred.CreatedAt
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "invalid_credential", message = ex.Message });
        }
    }

    [HttpPost("/api/v1/credentials/organization")]
    [HttpPost("organization")]
    public async Task<IActionResult> RegisterOrganizationCredential(
        [FromRoute] Guid? organizationId,
        [FromQuery] Guid? userId,
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        [FromBody] RegisterOrganizationCredentialRequest request,
        CancellationToken ct)
    {
        try
        {
            var effectiveOrgId = !string.IsNullOrWhiteSpace(request.OrganizationId)
                ? request.OrganizationId
                : organizationId?.ToString() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(effectiveOrgId))
            {
                return BadRequest(new { error = "missing_organization", message = "Organization ID is required." });
            }

            var effectiveUserId = userId?.ToString()
                ?? (!string.IsNullOrWhiteSpace(headerUserId) ? headerUserId : request.UserId);

            if (string.IsNullOrWhiteSpace(effectiveUserId))
            {
                return BadRequest(new { error = "missing_user", message = "User ID is required." });
            }

            var normalizedRequest = request with { OrganizationId = effectiveOrgId };
            var cred = await _credentials.RegisterOrganizationCredentialAsync(normalizedRequest, effectiveUserId, ct);

            return Ok(new
            {
                cred.Id,
                cred.OrganizationId,
                cred.OwningUserId,
                cred.ProviderName,
                cred.Label,
                cred.Scope,
                cred.Policy,
                cred.IsRevoked,
                cred.CreatedAt
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = "invalid_argument", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "invalid_operation", message = ex.Message });
        }
    }

    [HttpGet("/api/v1/credentials/organization")]
    [HttpGet("organization")]
    public async Task<IActionResult> GetOrganizationCredentials(
        [FromRoute] Guid? organizationId,
        [FromQuery(Name = "organizationId")] Guid? queryOrgId,
        [FromQuery] Guid? orgId,
        [FromQuery] Guid? userId,
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        CancellationToken ct)
    {
        try
        {
            var targetOrgId = organizationId ?? queryOrgId ?? orgId;
            if (!targetOrgId.HasValue)
            {
                return BadRequest(new { error = "missing_organization", message = "Organization ID is required." });
            }

            var actorUserId = userId?.ToString() ?? headerUserId;
            var list = await _credentials.GetOrganizationCredentialsAsync(targetOrgId.Value.ToString(), actorUserId, ct);

            var safeDtos = list.Select(c => new
            {
                c.Id,
                c.OrganizationId,
                c.OwningUserId,
                c.ProviderName,
                c.Label,
                c.Scope,
                c.Policy,
                c.IsRevoked,
                c.CreatedAt,
                c.RevokedAt
            });

            return Ok(safeDtos);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
    }

    [HttpPut("/api/v1/credentials/organization/{credentialId:guid}/policy")]
    [HttpPut("organization/{credentialId:guid}/policy")]
    public async Task<IActionResult> UpdateCredentialPolicy(
        [FromRoute] Guid credentialId,
        [FromQuery] Guid? userId,
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        [FromBody] UpdateCredentialPolicyRequest request,
        CancellationToken ct)
    {
        try
        {
            var actorUserId = userId?.ToString() ?? headerUserId;
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return BadRequest(new { error = "missing_user", message = "User ID is required." });
            }

            var cred = await _credentials.UpdatePolicyAsync(credentialId.ToString(), request.Policy, actorUserId, ct);
            return Ok(new
            {
                cred.Id,
                cred.OrganizationId,
                cred.OwningUserId,
                cred.ProviderName,
                cred.Label,
                cred.Scope,
                cred.Policy,
                cred.IsRevoked,
                cred.CreatedAt
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = "not_found", message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = "invalid_operation", message = ex.Message });
        }
    }

    [HttpGet("/api/v1/credentials/accessible")]
    [HttpGet("accessible")]
    public async Task<IActionResult> GetAccessibleCredentials(
        [FromRoute] Guid? organizationId,
        [FromQuery(Name = "organizationId")] Guid? queryOrgId,
        [FromQuery] Guid? orgId,
        [FromQuery] Guid? userId,
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        CancellationToken ct)
    {
        try
        {
            var targetOrgId = organizationId ?? queryOrgId ?? orgId;
            if (!targetOrgId.HasValue)
            {
                return BadRequest(new { error = "missing_organization", message = "Organization ID is required." });
            }

            var actorUserId = userId?.ToString() ?? headerUserId;
            if (string.IsNullOrWhiteSpace(actorUserId))
            {
                return BadRequest(new { error = "missing_user", message = "User ID is required." });
            }

            var list = await _credentials.GetAccessibleCredentialsAsync(actorUserId, targetOrgId.Value.ToString(), ct);

            var safeDtos = list.Select(c => new
            {
                c.Id,
                c.OrganizationId,
                c.OwningUserId,
                c.ProviderName,
                c.Label,
                c.Scope,
                c.Policy,
                c.IsRevoked,
                c.CreatedAt,
                c.RevokedAt
            });

            return Ok(safeDtos);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
    }

    [HttpDelete("{credentialId:guid}")]
    [HttpDelete("/api/v1/credentials/organization/{credentialId:guid}")]
    [HttpDelete("organization/{credentialId:guid}")]
    public async Task<IActionResult> RevokeCredential(
        [FromRoute] Guid? organizationId,
        [FromRoute] Guid credentialId,
        [FromQuery] Guid? userId,
        [FromHeader(Name = "X-User-Id")] string? headerUserId,
        CancellationToken ct)
    {
        try
        {
            var effectiveUserId = userId?.ToString() ?? headerUserId;
            if (string.IsNullOrWhiteSpace(effectiveUserId) || !Guid.TryParse(effectiveUserId, out var userGuid))
            {
                return BadRequest(new { error = "missing_user", message = "Valid User ID is required." });
            }

            await _credentials.RevokeCredentialAsync(credentialId, userGuid, ct);
            return Ok(new { status = "revoked", credentialId });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = "not_found", message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
    }
}
