namespace MuniClaw.Core.Contracts.Tasks;

public enum TaskStatus
{
    Open,
    Archived
}

public enum TaskRunStatus
{
    Queued,
    Preparing,
    Running,
    AwaitingInput,
    Completed,
    Failed,
    Cancelling,
    Cancelled
}

public static class TaskRunStateTransitions
{
    private static readonly HashSet<TaskRunStatus> TerminalStatuses =
    [
        TaskRunStatus.Completed,
        TaskRunStatus.Failed,
        TaskRunStatus.Cancelled
    ];

    private static readonly Dictionary<TaskRunStatus, HashSet<TaskRunStatus>> AllowedTransitions = new()
    {
        [TaskRunStatus.Queued] = [TaskRunStatus.Preparing, TaskRunStatus.Cancelling, TaskRunStatus.Cancelled],
        [TaskRunStatus.Preparing] = [TaskRunStatus.Running, TaskRunStatus.Failed, TaskRunStatus.Cancelling],
        [TaskRunStatus.Running] = [TaskRunStatus.AwaitingInput, TaskRunStatus.Completed, TaskRunStatus.Failed, TaskRunStatus.Cancelling],
        [TaskRunStatus.AwaitingInput] = [TaskRunStatus.Running, TaskRunStatus.Cancelling, TaskRunStatus.Failed],
        [TaskRunStatus.Cancelling] = [TaskRunStatus.Cancelled, TaskRunStatus.Failed],
        [TaskRunStatus.Completed] = [],
        [TaskRunStatus.Failed] = [],
        [TaskRunStatus.Cancelled] = []
    };

    public static bool IsTerminal(TaskRunStatus status) => TerminalStatuses.Contains(status);

    public static bool CanTransition(TaskRunStatus from, TaskRunStatus to)
    {
        return AllowedTransitions.TryGetValue(from, out var targets) && targets.Contains(to);
    }

    public static void ValidateTransition(TaskRunStatus from, TaskRunStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new InvalidOperationException($"Invalid TaskRun transition from {from} to {to}. Terminal states cannot transition or restart.");
        }
    }
}
