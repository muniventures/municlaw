using MuniClaw.Core.Contracts.Tasks;
using MuniClaw.Core.Data;
using MuniClaw.Core.Models;
using TaskStatus = MuniClaw.Core.Contracts.Tasks.TaskStatus;

namespace MuniClaw.Core.Services;

public sealed record CreateTaskRequest
{
    public required Guid OrganizationId { get; init; }
    public required Guid ProjectId { get; init; }
    public required Guid UserId { get; init; }
    public required string Title { get; init; }
    public required string BaseBranch { get; init; }
    public string? BaseCommitSha { get; init; }
    public required Guid ProviderCredentialReferenceId { get; init; }
    public required string Model { get; init; }
    public required string Instruction { get; init; }
    public required string HarnessVersion { get; init; }
    public decimal? MaxBudgetUsd { get; init; }
    public string? IdempotencyKey { get; init; }
    public bool StrictConcurrencyLimit { get; init; } = false;
}

public interface ITaskLifecycleService
{
    Task<(TaskEntity Task, TaskRun Run)> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct);
    Task<TaskRun> CreateFollowUpRunAsync(Guid taskId, Guid userId, string instruction, CancellationToken ct);
    Task CancelRunAsync(Guid runId, Guid userId, string reason, CancellationToken ct);
    Task CancelTaskAsync(Guid taskId, Guid userId, string reason, CancellationToken ct);
    Task ReconcileTerminalRunAsync(Guid runId, TaskRunStatus terminalStatus, string? failureReason = null, CancellationToken ct = default);
    Task ReconcileTerminatedRunsAsync(CancellationToken ct = default);
    Task<bool> HasActiveRunningTaskInOrganizationAsync(Guid organizationId, CancellationToken ct);
    Task<IReadOnlyList<TaskEntity>> ListTasksAsync(Guid organizationId, CancellationToken ct);
    Task<TaskEntity?> GetTaskAsync(Guid taskId, Guid organizationId, CancellationToken ct);
    Task<TaskRun?> GetRunAsync(Guid runId, Guid organizationId, CancellationToken ct);
    ITaskQueueService QueueService { get; }
}

public sealed class TaskLifecycleService : ITaskLifecycleService
{
    private readonly IMuniClawStore _store;
    private readonly IOrganizationAuthorizationService _auth;
    private readonly ITaskQueueService _queueService;
    private readonly object _lock = new();

    public ITaskQueueService QueueService => _queueService;

    public TaskLifecycleService(
        IMuniClawStore store,
        IOrganizationAuthorizationService auth,
        ITaskQueueService? queueService = null)
    {
        _store = store;
        _auth = auth;
        _queueService = queueService ?? new TaskQueueService(store);
    }

    public async Task<(TaskEntity Task, TaskRun Run)> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct)
    {
        if (!await _auth.CanAccessOrganizationAsync(request.UserId, request.OrganizationId, ct))
        {
            throw new UnauthorizedAccessException($"User {request.UserId} cannot access organization {request.OrganizationId}.");
        }

        if (!_store.Projects.TryGetValue(request.ProjectId, out var project) || project.OrganizationId != request.OrganizationId)
        {
            throw new KeyNotFoundException($"Project {request.ProjectId} was not found in organization {request.OrganizationId}.");
        }

        if (!_store.ProviderCredentials.TryGetValue(request.ProviderCredentialReferenceId, out var cred) ||
            cred.OrganizationId != request.OrganizationId || cred.IsRevoked)
        {
            throw new InvalidOperationException("Valid active provider credential reference is required.");
        }

        if (cred.Scope == CredentialScope.Organization)
        {
            var policy = cred.Policy ?? new OrganizationCredentialPolicy();

            if (policy.AdminOnly)
            {
                var isAdmin = await _auth.IsOrganizationAdminAsync(request.UserId, request.OrganizationId, ct);
                if (!isAdmin)
                {
                    throw new InvalidOperationException("Organization credential policy restricts usage to organization administrators.");
                }
            }

            if (!IsModelAllowed(request.Model, policy.AllowedModels))
            {
                throw new InvalidOperationException($"Requested model '{request.Model}' is not allowed by organization credential policy.");
            }

            if (policy.MonthlySpendLimitUsd.HasValue && policy.CurrentSpendUsd >= policy.MonthlySpendLimitUsd.Value)
            {
                throw new InvalidOperationException($"Organization credential monthly spend limit of ${policy.MonthlySpendLimitUsd.Value:F2} exceeded (current spend: ${policy.CurrentSpendUsd:F2}).");
            }
        }

        lock (_lock)
        {
            // Idempotent duplicate check: if a task with the exact title already exists created in the last 60s
            var existingTask = _store.Tasks.Values.FirstOrDefault(t =>
                t.OrganizationId == request.OrganizationId &&
                t.ProjectId == request.ProjectId &&
                t.Title == request.Title &&
                t.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));

            if (existingTask != null)
            {
                var latestRun = _store.TaskRuns.Values
                    .Where(r => r.TaskId == existingTask.Id)
                    .OrderByDescending(r => r.RunIndex)
                    .First();
                return (existingTask, latestRun);
            }

            var maxConcurrent = _store.Organizations.TryGetValue(request.OrganizationId, out var org)
                ? org.MaxConcurrentRuns
                : 2;

            var activeRunsCount = _store.TaskRuns.Values.Count(r =>
                r.OrganizationId == request.OrganizationId &&
                TaskQueueService.IsActiveExecuting(r.Status));

            if (maxConcurrent == 1 && request.StrictConcurrencyLimit && activeRunsCount >= 1)
            {
                throw new InvalidOperationException("Organization concurrency limit of 1 run reached (strict concurrency rejection).");
            }

            var taskId = Guid.NewGuid();
            var taskBranch = $"municlaw/task-{taskId:N}";

            var task = new TaskEntity
            {
                Id = taskId,
                OrganizationId = request.OrganizationId,
                ProjectId = request.ProjectId,
                Title = request.Title,
                Status = TaskStatus.Open,
                TaskBranch = taskBranch,
                BaseBranch = request.BaseBranch,
                BaseCommitSha = request.BaseCommitSha,
                CreatedByUserId = request.UserId,
                CreatedAt = DateTimeOffset.UtcNow
            };

            var runId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            int? queuePos = null;
            if (activeRunsCount >= maxConcurrent)
            {
                var queuedCount = _store.TaskRuns.Values.Count(r =>
                    r.OrganizationId == request.OrganizationId &&
                    r.Status == TaskRunStatus.Queued);
                queuePos = queuedCount + 1;
            }

            var run = new TaskRun
            {
                Id = runId,
                TaskId = taskId,
                OrganizationId = request.OrganizationId,
                RunIndex = 1,
                Status = TaskRunStatus.Queued,
                QueuePosition = queuePos,
                ProviderCredentialReferenceId = request.ProviderCredentialReferenceId,
                ResolvedModel = request.Model,
                Instruction = request.Instruction,
                HarnessVersion = request.HarnessVersion,
                MaxBudgetUsd = request.MaxBudgetUsd,
                CreatedAt = now,
                StartedAt = null
            };

            _store.Tasks[task.Id] = task;
            _store.TaskRuns[run.Id] = run;

            return (task, run);
        }
    }

    public async Task<TaskRun> CreateFollowUpRunAsync(Guid taskId, Guid userId, string instruction, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_store.Tasks.TryGetValue(taskId, out var task))
            {
                throw new KeyNotFoundException($"Task {taskId} was not found.");
            }

            if (!_auth.CanAccessOrganizationAsync(userId, task.OrganizationId, ct).GetAwaiter().GetResult())
            {
                throw new UnauthorizedAccessException($"User {userId} cannot access organization {task.OrganizationId}.");
            }

            var runs = _store.TaskRuns.Values.Where(r => r.TaskId == taskId).OrderBy(r => r.RunIndex).ToList();
            var latestRun = runs.Last();

            // Cannot follow up if a run is currently active
            if (!TaskRunStateTransitions.IsTerminal(latestRun.Status))
            {
                throw new InvalidOperationException($"Cannot create a follow-up while run {latestRun.Id} is still in progress ({latestRun.Status}).");
            }

            if (_store.ProviderCredentials.TryGetValue(latestRun.ProviderCredentialReferenceId, out var cred))
            {
                if (cred.IsRevoked)
                {
                    throw new InvalidOperationException("Provider credential has been revoked.");
                }

                if (cred.Scope == CredentialScope.Organization)
                {
                    var policy = cred.Policy ?? new OrganizationCredentialPolicy();
                    if (policy.AdminOnly && !_auth.IsOrganizationAdminAsync(userId, task.OrganizationId, ct).GetAwaiter().GetResult())
                    {
                        throw new InvalidOperationException("Organization credential policy restricts usage to organization administrators.");
                    }

                    if (policy.MonthlySpendLimitUsd.HasValue && policy.CurrentSpendUsd >= policy.MonthlySpendLimitUsd.Value)
                    {
                        throw new InvalidOperationException($"Organization credential monthly spend limit of ${policy.MonthlySpendLimitUsd.Value:F2} exceeded (current spend: ${policy.CurrentSpendUsd:F2}).");
                    }
                }
            }


            var queuedCount = _store.TaskRuns.Values.Count(r =>
                r.OrganizationId == task.OrganizationId &&
                r.Status == TaskRunStatus.Queued);

            var newRun = new TaskRun
            {
                Id = Guid.NewGuid(),
                TaskId = taskId,
                OrganizationId = task.OrganizationId,
                RunIndex = latestRun.RunIndex + 1,
                Status = TaskRunStatus.Queued,
                QueuePosition = queuedCount + 1,
                ProviderCredentialReferenceId = latestRun.ProviderCredentialReferenceId,
                ResolvedModel = latestRun.ResolvedModel,
                Instruction = instruction,
                HarnessVersion = latestRun.HarnessVersion,
                MaxBudgetUsd = latestRun.MaxBudgetUsd,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _store.TaskRuns[newRun.Id] = newRun;
            return newRun;
        }
    }

    public async Task CancelTaskAsync(Guid taskId, Guid userId, string reason, CancellationToken ct)
    {
        Guid? runId = null;
        lock (_lock)
        {
            if (!_store.Tasks.TryGetValue(taskId, out var task))
            {
                throw new KeyNotFoundException($"Task {taskId} was not found.");
            }

            var latestRun = _store.TaskRuns.Values
                .Where(r => r.TaskId == taskId)
                .OrderByDescending(r => r.RunIndex)
                .FirstOrDefault();

            if (latestRun != null)
            {
                runId = latestRun.Id;
            }
        }

        if (runId.HasValue)
        {
            await CancelRunAsync(runId.Value, userId, reason, ct);
        }
    }

    public async Task CancelRunAsync(Guid runId, Guid userId, string reason, CancellationToken ct)
    {
        Guid orgId;
        bool isTerminal;
        lock (_lock)
        {
            if (!_store.TaskRuns.TryGetValue(runId, out var run))
            {
                throw new KeyNotFoundException($"Run {runId} was not found.");
            }

            if (!_auth.CanAccessOrganizationAsync(userId, run.OrganizationId, ct).GetAwaiter().GetResult())
            {
                throw new UnauthorizedAccessException($"User {userId} cannot access organization {run.OrganizationId}.");
            }

            if (TaskRunStateTransitions.IsTerminal(run.Status))
            {
                orgId = run.OrganizationId;
                isTerminal = true;
            }
            else
            {
                TaskRunStateTransitions.ValidateTransition(run.Status, TaskRunStatus.Cancelling);
                run.Status = TaskRunStatus.Cancelling;
                run.FailureReason = $"Cancelled by user: {reason}";

                // Immediate transition if queued, otherwise supervisor will acknowledge cancellation
                if (run.Status == TaskRunStatus.Cancelling && run.LeaseToken == null)
                {
                    run.Status = TaskRunStatus.Cancelled;
                    run.CompletedAt = DateTimeOffset.UtcNow;
                }

                orgId = run.OrganizationId;
                isTerminal = TaskRunStateTransitions.IsTerminal(run.Status);
            }
        }

        if (isTerminal)
        {
            await _queueService.PromoteNextQueuedTaskAsync(orgId, ct);
        }
    }

    public async Task ReconcileTerminalRunAsync(Guid runId, TaskRunStatus terminalStatus, string? failureReason = null, CancellationToken ct = default)
    {
        Guid orgId;
        lock (_lock)
        {
            if (!_store.TaskRuns.TryGetValue(runId, out var run))
            {
                return;
            }

            if (!TaskRunStateTransitions.IsTerminal(terminalStatus))
            {
                throw new ArgumentException($"Status {terminalStatus} is not a terminal state.", nameof(terminalStatus));
            }

            if (TaskRunStateTransitions.IsTerminal(run.Status))
            {
                return;
            }

            TaskRunStateTransitions.ValidateTransition(run.Status, terminalStatus);
            run.Status = terminalStatus;
            run.CompletedAt = DateTimeOffset.UtcNow;
            if (failureReason != null)
            {
                run.FailureReason = failureReason;
            }
            orgId = run.OrganizationId;
        }

        await _queueService.PromoteNextQueuedTaskAsync(orgId, ct);
    }

    public async Task ReconcileTerminatedRunsAsync(CancellationToken ct = default)
    {
        List<Guid> orgIdsToPromote;
        lock (_lock)
        {
            orgIdsToPromote = _store.Organizations.Keys.ToList();
        }

        foreach (var orgId in orgIdsToPromote)
        {
            await _queueService.PromoteNextQueuedTaskAsync(orgId, ct);
        }
    }

    public Task<bool> HasActiveRunningTaskInOrganizationAsync(Guid organizationId, CancellationToken ct)
    {
        var hasActive = _store.TaskRuns.Values.Any(r =>
            r.OrganizationId == organizationId &&
            (r.Status == TaskRunStatus.Preparing ||
             r.Status == TaskRunStatus.Running ||
             r.Status == TaskRunStatus.AwaitingInput ||
             r.Status == TaskRunStatus.Cancelling));

        return Task.FromResult(hasActive);
    }

    public Task<IReadOnlyList<TaskEntity>> ListTasksAsync(Guid organizationId, CancellationToken ct)
    {
        var list = _store.Tasks.Values
            .Where(t => t.OrganizationId == organizationId)
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<TaskEntity>>(list);
    }

    public Task<TaskEntity?> GetTaskAsync(Guid taskId, Guid organizationId, CancellationToken ct)
    {
        if (_store.Tasks.TryGetValue(taskId, out var task) && task.OrganizationId == organizationId)
        {
            return Task.FromResult<TaskEntity?>(task);
        }
        return Task.FromResult<TaskEntity?>(null);
    }

    public Task<TaskRun?> GetRunAsync(Guid runId, Guid organizationId, CancellationToken ct)
    {
        if (_store.TaskRuns.TryGetValue(runId, out var run) && run.OrganizationId == organizationId)
        {
            return Task.FromResult<TaskRun?>(run);
        }
        return Task.FromResult<TaskRun?>(null);
    }

    private static bool IsModelAllowed(string requestedModel, IReadOnlyList<string>? allowedModels)
    {
        if (allowedModels == null || allowedModels.Count == 0)
        {
            return true;
        }

        foreach (var pattern in allowedModels)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                continue;
            }

            var trimmed = pattern.Trim();
            if (trimmed == "*")
            {
                return true;
            }

            if (string.Equals(requestedModel, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (trimmed.EndsWith('*'))
            {
                var prefix = trimmed[..^1];
                if (requestedModel.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (requestedModel.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

