using System.Net;
using System.Text;
using System.Text.Json;

namespace MuniClaw.Core.Integrations;

public interface IOpenBaoClient
{
    Task StoreSecretAsync(string path, IReadOnlyDictionary<string, string> secretData, CancellationToken ct = default);

    Task<Dictionary<string, string>?> GetSecretAsync(string path, CancellationToken ct = default);

    Task DeleteSecretAsync(string path, CancellationToken ct = default);
}

public sealed class OpenBaoHttpClient : IOpenBaoClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly string _vaultBaseUrl;
    private readonly string _token;

    public OpenBaoHttpClient(HttpClient httpClient, string vaultBaseUrl, string token)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (string.IsNullOrWhiteSpace(vaultBaseUrl))
        {
            throw new ArgumentException("Vault base URL cannot be null or whitespace.", nameof(vaultBaseUrl));
        }
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Vault token cannot be null or whitespace.", nameof(token));
        }

        _vaultBaseUrl = vaultBaseUrl.TrimEnd('/');
        _token = token;
    }

    public async Task StoreSecretAsync(string path, IReadOnlyDictionary<string, string> secretData, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(secretData);
        var normalizedPath = NormalizePath(path);
        var requestUri = $"{_vaultBaseUrl}/v1/secret/data/{normalizedPath}";

        var payload = new { data = secretData };
        var json = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        using var response = await SendAuthorizedRequestAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(Redact($"OpenBao store secret failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {errorBody}"), null, response.StatusCode);
        }
    }


    public async Task<Dictionary<string, string>?> GetSecretAsync(string path, CancellationToken ct = default)
    {
        var normalizedPath = NormalizePath(path);
        var requestUri = $"{_vaultBaseUrl}/v1/secret/data/{normalizedPath}";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        using var response = await SendAuthorizedRequestAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(Redact($"OpenBao get secret failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {errorBody}"), null, response.StatusCode);
        }

        var content = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        using var doc = JsonDocument.Parse(content);
        if (doc.RootElement.TryGetProperty("data", out var dataElement) &&
            dataElement.TryGetProperty("data", out var innerData) &&
            innerData.ValueKind == JsonValueKind.Object)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var prop in innerData.EnumerateObject())
            {
                result[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString()!
                    : prop.Value.ToString();
            }
            return result;
        }

        return null;
    }

    public async Task DeleteSecretAsync(string path, CancellationToken ct = default)
    {
        var normalizedPath = NormalizePath(path);
        var requestUri = $"{_vaultBaseUrl}/v1/secret/metadata/{normalizedPath}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, requestUri);
        using var response = await SendAuthorizedRequestAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(Redact($"OpenBao delete secret failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {errorBody}"), null, response.StatusCode);
        }
    }

    private async Task<HttpResponseMessage> SendAuthorizedRequestAsync(HttpRequestMessage request, CancellationToken ct)
    {
        request.Headers.TryAddWithoutValidation("X-Vault-Token", _token);
        try
        {
            return await _httpClient.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException(Redact(ex.Message), ex.InnerException != null ? new Exception(Redact(ex.InnerException.Message)) : null, ex.StatusCode);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(Redact(ex.Message), ex);
        }
    }

    private string Redact(string message)
    {
        if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(_token))
        {
            return message;
        }
        return message.Replace(_token, "[REDACTED]");
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Secret path cannot be null or empty.", nameof(path));
        }

        var trimmed = path.Trim('/');
        if (trimmed.StartsWith("v1/secret/data/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["v1/secret/data/".Length..];
        }
        else if (trimmed.StartsWith("v1/secret/metadata/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["v1/secret/metadata/".Length..];
        }
        else if (trimmed.StartsWith("secret/data/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["secret/data/".Length..];
        }
        else if (trimmed.StartsWith("secret/metadata/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["secret/metadata/".Length..];
        }
        else if (trimmed.StartsWith("secret/", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed["secret/".Length..];
        }

        return trimmed.Trim('/');
    }
}
