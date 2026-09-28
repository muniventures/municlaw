import * as React from "react";
import { Link } from "react-router";
import {
  Clock,
  GitBranch,
  PlayCircle,
  CheckCircle,
  XCircle,
  AlertCircle,
  ArrowRight,
  Plus,
  Search,
} from "lucide-react";
import type { TaskEntity, TaskItem, TaskRunStatus } from "@/api/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";

interface TaskListProps {
  tasks: (TaskEntity | TaskItem)[];
  isLoading: boolean;
  onOpenCreateModal: () => void;
}

export function TaskList({
  tasks,
  isLoading,
  onOpenCreateModal,
}: TaskListProps) {
  const [search, setSearch] = React.useState("");
  const [statusFilter, setStatusFilter] = React.useState<string>("all");

  const filteredTasks = tasks.filter((t) => {
    const matchesSearch =
      t.title.toLowerCase().includes(search.toLowerCase()) ||
      t.taskBranch.toLowerCase().includes(search.toLowerCase());
    
    if (!matchesSearch) return false;
    if (statusFilter === "all") return true;

    const latestRun = t.runs?.[t.runs.length - 1];
    const runStatus = latestRun?.status ?? (t.queuePosition ? "Queued" : "Queued");
    return runStatus.toLowerCase() === statusFilter.toLowerCase();
  });

  const getStatusBadge = (status?: TaskRunStatus | string, queuePosition?: number | null) => {
    const s = status?.toLowerCase();
    if (s === "queued") {
      const label = queuePosition != null && queuePosition > 0
        ? `Queued #${queuePosition}`
        : "Queued";
      return (
        <Badge
          variant="secondary"
          className="gap-1 bg-amber-500/15 text-amber-700 dark:text-amber-400 border border-amber-500/30 font-medium"
        >
          <Clock className="h-3 w-3" />
          {label}
        </Badge>
      );
    }

    switch (status) {
      case "Running":
      case "Preparing":
        return (
          <Badge variant="info" className="gap-1 animate-pulse">
            <PlayCircle className="h-3 w-3" />
            {status}
          </Badge>
        );
      case "AwaitingInput":
        return (
          <Badge variant="warning" className="gap-1">
            <AlertCircle className="h-3 w-3" />
            Awaiting Approval
          </Badge>
        );
      case "Completed":
        return (
          <Badge variant="success" className="gap-1">
            <CheckCircle className="h-3 w-3" />
            Completed
          </Badge>
        );
      case "Failed":
        return (
          <Badge variant="destructive" className="gap-1">
            <XCircle className="h-3 w-3" />
            Failed
          </Badge>
        );
      case "Cancelling":
      case "Cancelled":
        return (
          <Badge variant="secondary" className="gap-1">
            <Clock className="h-3 w-3" />
            {status}
          </Badge>
        );
      default: {
        const label = queuePosition != null && queuePosition > 0
          ? `Queued #${queuePosition}`
          : "Queued";
        return (
          <Badge
            variant="secondary"
            className="gap-1 bg-amber-500/15 text-amber-700 dark:text-amber-400 border border-amber-500/30 font-medium"
          >
            <Clock className="h-3 w-3" />
            {label}
          </Badge>
        );
      }
    }
  };

  return (
    <div className="space-y-4">
      {/* Top action row */}
      <div className="flex flex-col sm:flex-row gap-3 items-stretch sm:items-center justify-between">
        <div className="flex flex-1 items-center gap-2 max-w-md">
          <div className="relative flex-1">
            <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
            <Input
              placeholder="Filter tasks by title or branch..."
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              className="pl-9"
              aria-label="Filter tasks"
            />
          </div>
          <select
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
            className="h-9 rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-xs focus-visible:outline-none focus-visible:ring-1 focus-visible:ring-ring"
            aria-label="Filter by run status"
          >
            <option value="all">All Statuses</option>
            <option value="running">Running</option>
            <option value="awaitinginput">Awaiting Input</option>
            <option value="completed">Completed</option>
            <option value="failed">Failed</option>
            <option value="queued">Queued</option>
          </select>
        </div>

        <Button onClick={onOpenCreateModal} className="gap-2 shrink-0">
          <Plus className="h-4 w-4" />
          <span>New Task</span>
        </Button>
      </div>

      {/* Task list table / cards */}
      {isLoading ? (
        <div className="p-8 text-center text-muted-foreground rounded-lg border border-dashed">
          Loading tasks...
        </div>
      ) : filteredTasks.length === 0 ? (
        <div className="p-12 text-center rounded-xl border border-dashed border-border bg-card">
          <GitBranch className="h-10 w-10 text-muted-foreground mx-auto mb-3 opacity-60" />
          <h3 className="text-base font-semibold text-foreground mb-1">
            No tasks found
          </h3>
          <p className="text-sm text-muted-foreground max-w-sm mx-auto mb-4">
            {search || statusFilter !== "all"
              ? "No tasks match your current filters."
              : "Launch an autonomous coding task in your dedicated organization VPS."}
          </p>
          <Button onClick={onOpenCreateModal} variant="outline" className="gap-2">
            <Plus className="h-4 w-4" />
            Create First Task
          </Button>
        </div>
      ) : (
        <div className="grid gap-3">
          {filteredTasks.map((task) => {
            const latestRun = task.runs?.[task.runs.length - 1];
            const queuePos = latestRun?.queuePosition ?? task.queuePosition;
            const runStatus = latestRun?.status ?? (queuePos ? "Queued" : undefined);
            return (
              <Link
                key={task.id}
                to={`/tasks/${task.id}`}
                className="group block p-4 rounded-xl border border-border bg-card hover:border-primary/50 hover:shadow-xs transition-all"
              >
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                  <div className="space-y-1.5 min-w-0">
                    <div className="flex items-center gap-2.5 flex-wrap">
                      <span className="font-semibold text-sm group-hover:text-primary transition-colors truncate">
                        {task.title}
                      </span>
                      {getStatusBadge(runStatus, queuePos)}
                    </div>
                    <div className="flex items-center gap-4 text-xs text-muted-foreground flex-wrap font-mono">
                      <span className="flex items-center gap-1.5">
                        <GitBranch className="h-3 w-3" />
                        {task.taskBranch}
                      </span>
                      <span>Base: {task.baseBranch}</span>
                      {latestRun?.resolvedModel && (
                        <span className="px-1.5 py-0.5 rounded bg-muted text-foreground text-[11px]">
                          {latestRun.resolvedModel}
                        </span>
                      )}
                      <span>Run #{latestRun?.runIndex ?? 1}</span>
                    </div>
                  </div>

                  <div className="flex items-center gap-3 self-end sm:self-center shrink-0">
                    <span className="text-xs text-muted-foreground font-mono">
                      {new Date(task.createdAt).toLocaleDateString(undefined, {
                        month: "short",
                        day: "numeric",
                        hour: "2-digit",
                        minute: "2-digit",
                      })}
                    </span>
                    <ArrowRight className="h-4 w-4 text-muted-foreground group-hover:text-primary group-hover:translate-x-0.5 transition-all" />
                  </div>
                </div>
              </Link>
            );
          })}
        </div>
      )}
    </div>
  );
}
