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
}

public interface ITaskLifecycleService
{
    Task<(TaskEntity Task, TaskRun Run)> CreateTaskAsync(CreateTaskRequest request, CancellationToken ct);
    Task<TaskRun> CreateFollowUpRunAsync(Guid taskId, Guid userId, string instruction, CancellationToken ct);
    Task CancelRunAsync(Guid runId, Guid userId, string reason, CancellationToken ct);
    Task<bool> HasActiveRunningTaskInOrganizationAsync(Guid organizationId, CancellationToken ct);
    Task<IReadOnlyList<TaskEntity>> ListTasksAsync(Guid organizationId, CancellationToken ct);
    Task<TaskEntity?> GetTaskAsync(Guid taskId, Guid organizationId, CancellationToken ct);
    Task<TaskRun?> GetRunAsync(Guid runId, Guid organizationId, CancellationToken ct);
}

public sealed class TaskLifecycleService : ITaskLifecycleService
{
    private readonly IMuniClawStore _store;
    private readonly IOrganizationAuthorizationService _auth;
    private readonly object _lock = new();

    public TaskLifecycleService(IMuniClawStore store, IOrganizationAuthorizationService auth)
    {
        _store = store;
        _auth = auth;
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
            var run = new TaskRun
            {
                Id = runId,
                TaskId = taskId,
                OrganizationId = request.OrganizationId,
                RunIndex = 1,
                Status = TaskRunStatus.Queued,
                ProviderCredentialReferenceId = request.ProviderCredentialReferenceId,
                ResolvedModel = request.Model,
                Instruction = request.Instruction,
                HarnessVersion = request.HarnessVersion,
                MaxBudgetUsd = request.MaxBudgetUsd,
                CreatedAt = DateTimeOffset.UtcNow
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

            var newRun = new TaskRun
            {
                Id = Guid.NewGuid(),
                TaskId = taskId,
                OrganizationId = task.OrganizationId,
                RunIndex = latestRun.RunIndex + 1,
                Status = TaskRunStatus.Queued,
                ProviderCredentialReferenceId = latestRun.ProviderCredentialReferenceId,
                ResolvedModel = latestRun.ResolvedModel,
                Instruction = instruction,
                HarnessVersion = latestRun.HarnessVersion,
                MaxBudgetUsd = latestRun.MaxBudgetUsd
            };

            _store.TaskRuns[newRun.Id] = newRun;
            return newRun;
        }
    }

    public async Task CancelRunAsync(Guid runId, Guid userId, string reason, CancellationToken ct)
    {
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
                return; // Already terminal
            }

            TaskRunStateTransitions.ValidateTransition(run.Status, TaskRunStatus.Cancelling);
            run.Status = TaskRunStatus.Cancelling;
            run.FailureReason = $"Cancelled by user: {reason}";

            // Immediate transition if queued, otherwise supervisor will acknowledge cancellation
            if (run.Status == TaskRunStatus.Cancelling && run.LeaseToken == null)
            {
                run.Status = TaskRunStatus.Cancelled;
                run.CompletedAt = DateTimeOffset.UtcNow;
            }
        }
        await Task.CompletedTask;
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
}
