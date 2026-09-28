using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MuniClaw.Core.Contracts.Harness;

namespace MuniClaw.Worker.Harness;

public sealed class OpenCodeHarnessAdapter : IHarnessAdapter
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OpenCodeHarnessAdapter> _logger;

    public TimeSpan LastMeasuredAbortLatency { get; private set; } = TimeSpan.Zero;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public OpenCodeHarnessAdapter(
        HttpClient httpClient,
        string serverPassword,
        ILogger<OpenCodeHarnessAdapter>? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger ?? NullLogger<OpenCodeHarnessAdapter>.Instance;

        var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"opencode:{serverPassword}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authHeader);
    }

    public async Task<HarnessSessionInfo> CreateSessionAsync(string workingDirectory, CancellationToken ct)
    {
        var payload = new { directory = workingDirectory };
        var response = await _httpClient.PostAsJsonAsync("session", payload, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var root = doc.RootElement;

        var sessionId = root.TryGetProperty("sessionId", out var sid) ? sid.GetString() :
                       root.TryGetProperty("id", out var id) ? id.GetString() : Guid.NewGuid().ToString("N");

        var slug = root.TryGetProperty("slug", out var s) ? s.GetString() ?? "default-session" : "default-session";
        var version = root.TryGetProperty("version", out var v) ? v.GetString() ?? "1.18.32" : "1.18.32";
        var directory = root.TryGetProperty("directory", out var d) ? d.GetString() ?? workingDirectory : workingDirectory;

        return new HarnessSessionInfo
        {
            SessionId = sessionId ?? Guid.NewGuid().ToString("N"),
            Slug = slug,
            Version = version,
            Directory = directory,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task SendPromptAsync(string sessionId, HarnessPrompt prompt, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync($"session/{sessionId}/prompt_async", prompt, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<HarnessDiffItem>> GetDiffAsync(string sessionId, CancellationToken ct)
    {
        var response = await _httpClient.GetAsync($"session/{sessionId}/diff", ct);
        response.EnsureSuccessStatusCode();

        var diff = await response.Content.ReadFromJsonAsync<IReadOnlyList<HarnessDiffItem>>(JsonOptions, ct);
        return diff ?? Array.Empty<HarnessDiffItem>();
    }

    public async Task<bool> AbortAsync(string sessionId, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var response = await _httpClient.PostAsync($"session/{sessionId}/abort", null, ct);
        sw.Stop();
        LastMeasuredAbortLatency = sw.Elapsed;

        _logger.LogInformation("OpenCode abort call for session {SessionId} completed in {LatencyMs:F2}ms (bound: <100ms)",
            sessionId, LastMeasuredAbortLatency.TotalMilliseconds);

        return response.IsSuccessStatusCode;
    }

    public async IAsyncEnumerable<HarnessRawEvent> SubscribeEventsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "event");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        string? currentEventType = null;

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line == null) break;

            if (line.StartsWith("event:", StringComparison.OrdinalIgnoreCase))
            {
                currentEventType = line.Substring("event:".Length).Trim();
            }
            else if (line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var json = line.Substring("data:".Length).Trim();
                if (string.IsNullOrEmpty(json)) continue;

                string? eventType = currentEventType;
                string? sessionId = null;

                try
                {
                    using var doc = JsonDocument.Parse(json);
                    if (eventType == null && doc.RootElement.TryGetProperty("type", out var typeProp))
                    {
                        eventType = typeProp.GetString();
                    }

                    if (doc.RootElement.TryGetProperty("sessionId", out var sessProp))
                    {
                        sessionId = sessProp.GetString();
                    }
                }
                catch
                {
                    // If JSON document parse fails for envelope, proceed with raw payload
                }

                yield return new HarnessRawEvent
                {
                    EventType = eventType ?? "Unknown",
                    SessionId = sessionId,
                    PayloadJson = json,
                    Timestamp = DateTimeOffset.UtcNow
                };

                currentEventType = null;
            }
        }
    }
}
