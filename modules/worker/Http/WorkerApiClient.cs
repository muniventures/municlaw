using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MuniClaw.Core.Contracts.Worker;

namespace MuniClaw.Worker.Http;

public sealed class WorkerApiClient : IWorkerApiClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public WorkerApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<WorkerClaimResponse> ClaimWorkAsync(WorkerClaimRequest request, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync("api/v1/worker/claim", request, JsonOptions, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<WorkerClaimResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Empty response received from worker claim endpoint.");
    }

    public async Task<WorkerHeartbeatResponse> HeartbeatAsync(WorkerHeartbeatRequest request, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync("api/v1/worker/heartbeat", request, JsonOptions, ct);
        if (response.StatusCode == HttpStatusCode.Gone)
        {
            return new WorkerHeartbeatResponse
            {
                IsLeaseValid = false,
                PendingCommands = Array.Empty<WorkerCommand>()
            };
        }

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WorkerHeartbeatResponse>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Empty response received from worker heartbeat endpoint.");
    }

    public async Task<WorkerEventBatchAck> UploadEventsAsync(WorkerEventBatchUpload upload, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync("api/v1/worker/events", upload, JsonOptions, ct);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            throw new InvalidOperationException("Fencing conflict: fencing token is obsolete or rejected by control plane.");
        }

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<WorkerEventBatchAck>(JsonOptions, ct);
        return result ?? throw new InvalidOperationException("Empty response received from worker events upload endpoint.");
    }
}
