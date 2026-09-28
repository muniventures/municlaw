import * as React from "react";
import { useNavigate } from "react-router";
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
