using MuniClaw.Core.Contracts.Worker;

namespace MuniClaw.Worker.Http;

public interface IWorkerApiClient
{
    Task<WorkerClaimResponse> ClaimWorkAsync(WorkerClaimRequest request, CancellationToken ct);
    Task<WorkerHeartbeatResponse> HeartbeatAsync(WorkerHeartbeatRequest request, CancellationToken ct);
    Task<WorkerEventBatchAck> UploadEventsAsync(WorkerEventBatchUpload upload, CancellationToken ct);
}
