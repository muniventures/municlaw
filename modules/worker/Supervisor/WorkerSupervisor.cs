using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MuniClaw.Core.Contracts.Harness;
using MuniClaw.Core.Contracts.Tasks;
using MuniClaw.Core.Contracts.Worker;
using MuniClaw.Worker.Configuration;
using MuniClaw.Worker.Harness;
using MuniClaw.Worker.Http;
using MuniClaw.Worker.Sandbox;

namespace MuniClaw.Worker.Supervisor;

public sealed class WorkerSupervisor : BackgroundService, IWorkerSupervisor
{
    private readonly WorkerOptions _options;
    private readonly IWorkerApiClient _apiClient;
    private readonly ITaskSandboxManager _sandboxManager;
    private readonly IHarnessRegistry _harnessRegistry;
    private readonly IWorkerEventBuffer _eventBuffer;
    private readonly ILogger<WorkerSupervisor> _logger;

    private Guid? _currentRunId;
    private string? _currentLeaseToken;
    private long _currentFencingToken;
    private long _lastCommandCursor;
    private bool _isActive;

    public bool IsActive => _isActive;
    public Guid? CurrentRunId => _currentRunId;
    public string? CurrentLeaseToken => _currentLeaseToken;
    public long CurrentFencingToken => _currentFencingToken;
    public long LastCommandCursor => _lastCommandCursor;
    public IHarnessRegistry HarnessRegistry => _harnessRegistry;

    public WorkerSupervisor(
        WorkerOptions options,
        IWorkerApiClient apiClient,
        ITaskSandboxManager sandboxManager,
        IHarnessRegistry harnessRegistry,
        IWorkerEventBuffer? eventBuffer = null,
        ILogger<WorkerSupervisor>? logger = null)
    {
        _options = options;
        _apiClient = apiClient;
        _sandboxManager = sandboxManager;
        _harnessRegistry = harnessRegistry;
        _eventBuffer = eventBuffer ?? new WorkerEventBuffer(options.MaxBufferedEvents);
        _logger = logger ?? NullLogger<WorkerSupervisor>.Instance;
    }

    public WorkerSupervisor(
        WorkerOptions options,
        IWorkerApiClient apiClient,
        ITaskSandboxManager sandboxManager,
        IOpenCodeSupervisor harnessSupervisor,
        IWorkerEventBuffer? eventBuffer = null,
        ILogger<WorkerSupervisor>? logger = null)
        : this(options, apiClient, sandboxManager, CreateRegistryWithOpenCode(harnessSupervisor), eventBuffer, logger)
    {
    }

    private static IHarnessRegistry CreateRegistryWithOpenCode(IOpenCodeSupervisor supervisor)
    {
        var registry = new HarnessRegistry();
        registry.Register(supervisor);
        return registry;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting MuniClaw worker supervisor for WorkerId {WorkerId}, Org {OrgId}",
            _options.WorkerId, _options.OrganizationId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var hadWork = await PollAndProcessOnceAsync(stoppingToken);
                if (!hadWork)
                {
                    await Task.Delay(_options.ClaimPollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in worker supervisor loop");
                try
                {
                    await Task.Delay(_options.ClaimPollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("Worker supervisor stopped.");
    }

    public async Task<bool> PollAndProcessOnceAsync(CancellationToken ct)
    {
        var claimReq = new WorkerClaimRequest
        {
            WorkerId = _options.WorkerId,
            OrganizationId = _options.OrganizationId,
            SupportedHarnessVersion = _options.SupportedHarnessVersion
        };

        var claim = await _apiClient.ClaimWorkAsync(claimReq, ct);
        if (!claim.HasWork || !claim.RunId.HasValue || !claim.TaskId.HasValue)
        {
            return false;
        }

        await ExecuteClaimedRunAsync(claim, ct);
        return true;
    }

    private async Task ExecuteClaimedRunAsync(WorkerClaimResponse claim, CancellationToken supervisorCt)
    {
        var runId = claim.RunId!.Value;
        var taskId = claim.TaskId!.Value;
        var fencingToken = claim.FencingToken;
        var initialLeaseToken = claim.LeaseToken ?? string.Empty;

        // Dynamically resolve harness supervisor using claim.HarnessType.ToString()
        var harnessSupervisor = _harnessRegistry.GetSupervisor(claim.HarnessType.ToString());
        if (!harnessSupervisor.IsRunning)
        {
            await harnessSupervisor.StartAsync(supervisorCt);
        }

        _isActive = true;
        _currentRunId = runId;
        _currentFencingToken = fencingToken;
        _currentLeaseToken = initialLeaseToken;
        _lastCommandCursor = 0;
        _eventBuffer.Reset();

        _logger.LogInformation("Claimed work for Task {TaskId}, Run {RunId}. Harness={HarnessType}, FencingToken={FencingToken}",
            taskId, runId, harnessSupervisor.HarnessType, fencingToken);

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(supervisorCt);
        var leaseLost = false;
        string? activeSessionId = null;

        // 1. Set up sandbox
        var sandbox = await _sandboxManager.CreateOrGetSandboxAsync(taskId, runId, runCts.Token);

        // 2. Buffer & upload initial preparing/running events
        _eventBuffer.Enqueue(taskId, runId, TaskEventType.StatusChanged, "{\"status\":\"Running\"}");
        await FlushEventsSafelyAsync(runId, fencingToken, ct: runCts.Token);

        // 3. Initialize harness session
        HarnessSessionInfo? session = null;
        try
        {
            session = await harnessSupervisor.Adapter.CreateSessionAsync(sandbox.WorktreeDirectory, runCts.Token);
            activeSessionId = session.SessionId;

            _eventBuffer.Enqueue(taskId, runId, TaskEventType.StepStarted,
                $"{{\"step\":1,\"sessionId\":\"{session.SessionId}\"}}");
            await FlushEventsSafelyAsync(runId, fencingToken, ct: runCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create harness session for task {TaskId}", taskId);
            _eventBuffer.Enqueue(taskId, runId, TaskEventType.Error,
                $"{{\"error\":\"Harness session initialization failed: {ex.Message}\"}}");
            await FlushEventsSafelyAsync(runId, fencingToken, ct: supervisorCt);
            await _sandboxManager.MarkRunCompletedAsync(taskId, runId, isSuccess: false, DateTimeOffset.UtcNow, default);
            CleanupRunState();
            return;
        }

        // 4. Handshake: Perform initial heartbeat to validate lease and sync pending commands
        try
        {
            var initialHb = await _apiClient.HeartbeatAsync(new WorkerHeartbeatRequest
            {
                WorkerId = _options.WorkerId,
                RunId = runId,
                FencingToken = fencingToken,
                CurrentLeaseToken = _currentLeaseToken ?? string.Empty,
                LastAcknowledgedCommandCursor = _lastCommandCursor
            }, runCts.Token);

            if (!initialHb.IsLeaseValid)
            {
                _logger.LogWarning("Initial heartbeat rejected or lease expired for Run {RunId}. Terminating immediately.", runId);
                leaseLost = true;
                runCts.Cancel();
            }
            else
            {
                if (!string.IsNullOrEmpty(initialHb.ExtendedLeaseToken))
                {
                    _currentLeaseToken = initialHb.ExtendedLeaseToken;
                }

                if (initialHb.PendingCommands != null && initialHb.PendingCommands.Count > 0)
                {
                    foreach (var cmd in initialHb.PendingCommands.OrderBy(c => c.CommandSequence))
                    {
                        await HandleCommandAsync(cmd, activeSessionId, harnessSupervisor.Adapter, runCts);
                        _lastCommandCursor = Math.Max(_lastCommandCursor, cmd.CommandSequence);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform initial heartbeat for Run {RunId}", runId);
        }

        // 5. Background Heartbeat & Command processing loop for ongoing lease extension
        var heartbeatTask = Task.Run(async () =>
        {
            while (!runCts.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_options.HeartbeatInterval, runCts.Token);

                    var hbReq = new WorkerHeartbeatRequest
                    {
                        WorkerId = _options.WorkerId,
                        RunId = runId,
                        FencingToken = fencingToken,
                        CurrentLeaseToken = _currentLeaseToken ?? string.Empty,
                        LastAcknowledgedCommandCursor = _lastCommandCursor
                    };

                    var hbResp = await _apiClient.HeartbeatAsync(hbReq, runCts.Token);

                    if (!hbResp.IsLeaseValid)
                    {
                        // Lease-loss termination! Stop all child processes and abort immediately
                        _logger.LogWarning("Heartbeat rejected or lease lost for Run {RunId}. Terminating execution immediately.", runId);
                        leaseLost = true;
                        runCts.Cancel();
                        break;
                    }

                    if (!string.IsNullOrEmpty(hbResp.ExtendedLeaseToken))
                    {
                        _currentLeaseToken = hbResp.ExtendedLeaseToken;
                    }

                    if (hbResp.PendingCommands != null && hbResp.PendingCommands.Count > 0)
                    {
                        foreach (var cmd in hbResp.PendingCommands.OrderBy(c => c.CommandSequence))
                        {
                            await HandleCommandAsync(cmd, activeSessionId, harnessSupervisor.Adapter, runCts);
                            _lastCommandCursor = Math.Max(_lastCommandCursor, cmd.CommandSequence);
                        }
                    }
                }
                catch (OperationCanceledException) when (runCts.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Heartbeat error during Run {RunId}", runId);
                }
            }
        });

        // 5. Execution & Event Streaming loop
        try
        {
            // Stream harness events while active
            await foreach (var rawEvt in harnessSupervisor.Adapter.SubscribeEventsAsync(runCts.Token))
            {
                var canonical = MapHarnessEvent(harnessSupervisor.HarnessType, rawEvt);
                if (canonical.HasValue)
                {
                    _eventBuffer.Enqueue(taskId, runId, canonical.Value, rawEvt.PayloadJson, rawEvt.Timestamp);
                    await FlushEventsSafelyAsync(runId, fencingToken, ct: runCts.Token);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Execution loop canceled for Run {RunId}", runId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Execution error in Run {RunId}", runId);
            _eventBuffer.Enqueue(taskId, runId, TaskEventType.Error, $"{{\"error\":\"{ex.Message}\"}}");
        }
        finally
        {
            runCts.Cancel();
            try
            {
                await heartbeatTask;
            }
            catch
            {
                // Heartbeat cancellation ignored
            }
        }

        // 6. Terminal resolution
        if (leaseLost)
        {
            _logger.LogWarning("Run {RunId} ended due to lease loss. Bypassing terminal event upload to prevent split-brain.", runId);
            await _sandboxManager.MarkRunCompletedAsync(taskId, runId, isSuccess: false, DateTimeOffset.UtcNow, default);
        }
        else
        {
            _eventBuffer.Enqueue(taskId, runId, TaskEventType.StatusChanged, "{\"status\":\"Completed\"}");
            await FlushEventsSafelyAsync(runId, fencingToken, ct: supervisorCt);
            await _sandboxManager.MarkRunCompletedAsync(taskId, runId, isSuccess: true, DateTimeOffset.UtcNow, default);
        }

        CleanupRunState();
    }

    private async Task HandleCommandAsync(WorkerCommand cmd, string? sessionId, IHarnessAdapter adapter, CancellationTokenSource runCts)
    {
        _logger.LogInformation("Handling worker command {Type} (Seq {Seq})", cmd.CommandType, cmd.CommandSequence);

        switch (cmd.CommandType)
        {
            case WorkerCommandType.Abort:
            case WorkerCommandType.Cancel:
                if (!string.IsNullOrEmpty(sessionId))
                {
                    await adapter.AbortAsync(sessionId, default);
                }
                runCts.Cancel();
                break;

            case WorkerCommandType.Resume:
            case WorkerCommandType.Reconcile:
                // Reconcile/resume hooks
                break;
        }
    }

    private static TaskEventType? MapHarnessEvent(string harnessType, HarnessRawEvent rawEvt)
    {
        TaskEventType? canonical = string.Equals(harnessType, "ClaudeCode", StringComparison.OrdinalIgnoreCase)
            ? ClaudeCodeEventMapper.MapToCanonical(rawEvt.EventType)
            : HarnessEventMapper.MapToCanonical(rawEvt.EventType);

        if (!canonical.HasValue)
        {
            if (Enum.TryParse<TaskEventType>(rawEvt.EventType, true, out var direct))
            {
                canonical = direct;
            }
            else
            {
                canonical = ClaudeCodeEventMapper.MapToCanonical(rawEvt.EventType)
                    ?? HarnessEventMapper.MapToCanonical(rawEvt.EventType);
            }
        }

        return canonical;
    }

    private async Task FlushEventsSafelyAsync(Guid runId, long fencingToken, CancellationToken ct)
    {
        try
        {
            if (_eventBuffer.PendingCount > 0)
            {
                await _eventBuffer.FlushAsync(_apiClient, _options.WorkerId, runId, fencingToken, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush events batch for Run {RunId}", runId);
        }
    }

    private void CleanupRunState()
    {
        _isActive = false;
        _currentRunId = null;
        _currentLeaseToken = null;
        _currentFencingToken = 0;
    }
}
