using Microsoft.AspNetCore.Mvc;
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

    [HttpDelete("{credentialId:guid}")]
    public async Task<IActionResult> RevokeCredential(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid credentialId,
        [FromQuery] Guid userId,
        CancellationToken ct)
    {
        try
        {
            await _credentials.RevokeCredentialAsync(credentialId, userId, ct);
            return Ok(new { status = "revoked", credentialId });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new { error = "forbidden", message = ex.Message });
        }
    }
}
