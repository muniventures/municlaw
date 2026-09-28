import * as React from "react";
import { useNavigate } from "react-router";
import { Layers } from "lucide-react";
import { TaskList } from "./components/TaskList";
import { TaskCreateModal } from "./components/TaskCreateModal";
import { listTasks } from "@/api/tasks";
import { listProjects } from "@/api/connections";
import { listCredentials } from "@/api/connections";
import type { TaskEntity, Project, ProviderCredentialReference } from "@/api/types";
import { getRuntimeConfig } from "@/core/config/runtime";

export function TasksModule() {
  const navigate = useNavigate();
  const [config] = React.useState(() => getRuntimeConfig());
  const [tasks, setTasks] = React.useState<TaskEntity[]>([]);
  const [projects, setProjects] = React.useState<Project[]>([]);
  const [credentials, setCredentials] = React.useState<ProviderCredentialReference[]>([]);
  const [isLoading, setIsLoading] = React.useState(true);
  const [createModalOpen, setCreateModalOpen] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  const fetchData = React.useCallback(async () => {
    try {
      setIsLoading(true);
      setError(null);
      const [fetchedTasks, fetchedProjects, fetchedCreds] = await Promise.all([
        listTasks(config.defaultOrganizationId).catch(() => []),
        listProjects(config.defaultOrganizationId).catch(() => []),
        listCredentials(config.defaultOrganizationId, config.defaultUserId).catch(() => []),
      ]);
      setTasks(fetchedTasks);
      setProjects(fetchedProjects);
      setCredentials(fetchedCreds);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to load dashboard data");
    } finally {
      setIsLoading(false);
    }
  }, [config.defaultOrganizationId, config.defaultUserId]);

  React.useEffect(() => {
    fetchData();
  }, [fetchData]);

  const handleTaskCreated = (taskId: string) => {
    navigate(`/tasks/${taskId}`);
  };

  const maxQuota = React.useMemo(() => {
    if (typeof localStorage !== "undefined") {
      const raw = localStorage.getItem(`municlaw_concurrency_${config.defaultOrganizationId}`);
      if (raw) {
        const parsed = parseInt(raw, 10);
        if (!isNaN(parsed) && parsed >= 1 && parsed <= 10) {
          return parsed;
        }
      }
    }
    return 2;
  }, [config.defaultOrganizationId]);

  const activeRunsCount = React.useMemo(() => {
    return tasks.filter((t) => {
      const latestRun = t.runs?.[t.runs.length - 1];
      const s = latestRun?.status?.toLowerCase();
      return s === "running" || s === "preparing" || s === "awaitinginput";
    }).length;
  }, [tasks]);

  const queuedRunsCount = React.useMemo(() => {
    return tasks.filter((t) => {
      const latestRun = t.runs?.[t.runs.length - 1];
      const s = latestRun?.status?.toLowerCase();
      return s === "queued" || (!latestRun && t.queuePosition);
    }).length;
  }, [tasks]);

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-bold tracking-tight text-foreground">
          Autonomous Coding Tasks
        </h1>
        <p className="text-sm text-muted-foreground">
          Deploy, monitor, review diffs, approve operations, and publish Pull Requests from your dedicated VPS workspace.
        </p>
      </div>

      {error && (
        <div className="p-4 rounded-xl bg-destructive/10 border border-destructive/20 text-destructive text-sm font-medium">
          {error}
        </div>
      )}

      {/* Concurrency Slot Progress Bar */}
      <div className="p-4 rounded-xl border border-border bg-card shadow-xs flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div className="space-y-1.5 flex-1 max-w-lg">
          <div className="flex items-center justify-between text-xs">
            <span className="font-semibold text-foreground flex items-center gap-1.5">
              <Layers className="h-4 w-4 text-primary" />
              Active Tasks: {activeRunsCount} / {maxQuota} slots in use
            </span>
            <span className="text-muted-foreground font-mono text-[11px]">
              {Math.min(100, Math.round((activeRunsCount / maxQuota) * 100))}% Capacity
            </span>
          </div>
          <div className="w-full bg-muted rounded-full h-2 overflow-hidden border border-border/40">
            <div
              className={`h-full transition-all duration-300 rounded-full ${
                activeRunsCount >= maxQuota ? "bg-amber-500" : "bg-primary"
              }`}
              style={{
                width: `${Math.min(100, Math.max(0, Math.round((activeRunsCount / maxQuota) * 100)))}%`,
              }}
            />
          </div>
        </div>

        <div className="flex items-center gap-2.5 text-xs shrink-0 font-mono">
          <div className="px-2.5 py-1 rounded-md bg-muted/60 text-muted-foreground">
            Active: <span className="font-semibold text-foreground">{activeRunsCount}</span>
          </div>
          <div className="px-2.5 py-1 rounded-md bg-muted/60 text-muted-foreground">
            Queued: <span className="font-semibold text-foreground">{queuedRunsCount}</span>
          </div>
          <div className="px-2.5 py-1 rounded-md bg-muted/60 text-muted-foreground">
            Quota: <span className="font-semibold text-foreground">{maxQuota}</span>
          </div>
        </div>
      </div>

      <TaskList
        tasks={tasks}
        isLoading={isLoading}
        onOpenCreateModal={() => setCreateModalOpen(true)}
      />

      <TaskCreateModal
        open={createModalOpen}
        onOpenChange={setCreateModalOpen}
        organizationId={config.defaultOrganizationId}
        userId={config.defaultUserId}
        projects={projects}
        credentials={credentials}
        onTaskCreated={handleTaskCreated}
      />
    </div>
  );
}
