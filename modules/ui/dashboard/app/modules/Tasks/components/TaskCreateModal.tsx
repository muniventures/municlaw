import * as React from "react";
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter } from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Select } from "@/components/ui/select";
import type { Project, ProviderCredentialReference } from "@/api/types";
import { createTask, type CreateTaskPayload } from "@/api/tasks";

interface TaskCreateModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  organizationId: string;
  userId: string;
  projects: Project[];
  credentials: ProviderCredentialReference[];
  onTaskCreated: (taskId: string) => void;
}

const SUPPORTED_MODELS = [
  { id: "claude-3-7-sonnet", name: "Anthropic Claude 3.7 Sonnet (Default)" },
  { id: "claude-3-5-sonnet", name: "Anthropic Claude 3.5 Sonnet" },
  { id: "gpt-4o", name: "OpenAI GPT-4o" },
  { id: "gpt-4.5-preview", name: "OpenAI GPT-4.5 Preview" },
];

export function TaskCreateModal({
  open,
  onOpenChange,
  organizationId,
  userId,
  projects,
  credentials,
  onTaskCreated,
}: TaskCreateModalProps) {
  const [projectId, setProjectId] = React.useState(projects[0]?.id || "");
  const [title, setTitle] = React.useState("");
  const [baseBranch, setBaseBranch] = React.useState(
    projects[0]?.defaultBaseBranch || "main"
  );
  const [model, setModel] = React.useState(SUPPORTED_MODELS[0].id);
  const [credentialId, setCredentialId] = React.useState(
    credentials[0]?.id || ""
  );
  const [instruction, setInstruction] = React.useState("");
  const [maxBudgetUsd, setMaxBudgetUsd] = React.useState<string>("5.00");
  const [isSubmitting, setIsSubmitting] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  React.useEffect(() => {
    if (projects.length > 0 && !projectId) {
      setProjectId(projects[0].id);
      setBaseBranch(projects[0].defaultBaseBranch || "main");
    }
  }, [projects, projectId]);

  React.useEffect(() => {
    if (credentials.length > 0 && !credentialId) {
      setCredentialId(credentials[0].id);
    }
  }, [credentials, credentialId]);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!title.trim() || !instruction.trim()) {
      setError("Please provide both a task title and instructions.");
      return;
    }
    if (!projectId) {
      setError("Please select a target repository project.");
      return;
    }
    if (!credentialId) {
      setError("Please register or select a provider API key in Connections.");
      return;
    }

    try {
      setIsSubmitting(true);
      setError(null);

      const payload: CreateTaskPayload = {
        projectId,
        userId,
        title: title.trim(),
        baseBranch: baseBranch.trim() || "main",
        providerCredentialReferenceId: credentialId,
        model,
        instruction: instruction.trim(),
        harnessVersion: "opencode-v1",
        maxBudgetUsd: maxBudgetUsd ? parseFloat(maxBudgetUsd) : undefined,
      };

      const result = await createTask(organizationId, payload);
      onOpenChange(false);
      onTaskCreated(result.task.id);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : "Failed to create task";
      setError(msg);
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent onClose={() => onOpenChange(false)} className="max-w-xl">
        <form onSubmit={handleSubmit}>
          <DialogHeader>
            <DialogTitle>Launch Autonomous Task</DialogTitle>
            <DialogDescription>
              Create an isolated branch on your dedicated VPS coding workspace.
            </DialogDescription>
          </DialogHeader>

          {error && (
            <div className="mb-4 p-3 rounded-lg bg-destructive/10 border border-destructive/20 text-destructive text-sm font-medium">
              {error}
            </div>
          )}

          <div className="space-y-4 py-2">
            {/* Title */}
            <div>
              <label htmlFor="task-title" className="block text-xs font-semibold text-foreground mb-1.5">
                Task Title
              </label>
              <Input
                id="task-title"
                placeholder="e.g., Fix database connection retry logic in payment service"
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                required
              />
            </div>

            {/* Project & Base Branch */}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <div>
                <label htmlFor="task-project" className="block text-xs font-semibold text-foreground mb-1.5">
                  Repository Project
                </label>
                <Select
                  id="task-project"
                  value={projectId}
                  onChange={(e) => {
                    setProjectId(e.target.value);
                    const selected = projects.find((p) => p.id === e.target.value);
                    if (selected) setBaseBranch(selected.defaultBaseBranch);
                  }}
                >
                  {projects.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name}
                    </option>
                  ))}
                </Select>
              </div>

              <div>
                <label htmlFor="task-branch" className="block text-xs font-semibold text-foreground mb-1.5">
                  Base Branch
                </label>
                <Input
                  id="task-branch"
                  value={baseBranch}
                  onChange={(e) => setBaseBranch(e.target.value)}
                  placeholder="main"
                  required
                />
              </div>
            </div>

            {/* Model & BYOK Key */}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <div>
                <label htmlFor="task-model" className="block text-xs font-semibold text-foreground mb-1.5">
                  AI Model
                </label>
                <Select
                  id="task-model"
                  value={model}
                  onChange={(e) => setModel(e.target.value)}
                >
                  {SUPPORTED_MODELS.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.name}
                    </option>
                  ))}
                </Select>
              </div>

              <div>
                <label htmlFor="task-credential" className="block text-xs font-semibold text-foreground mb-1.5">
                  Provider Key (BYOK)
                </label>
                {credentials.length === 0 ? (
                  <div className="text-xs text-amber-600 bg-amber-500/10 p-2 rounded border border-amber-500/20">
                    No keys registered. Go to Connections to add your write-only key.
                  </div>
                ) : (
                  <Select
                    id="task-credential"
                    value={credentialId}
                    onChange={(e) => setCredentialId(e.target.value)}
                  >
                    {credentials.map((c) => (
                      <option key={c.id} value={c.id}>
                        {c.label} ({c.providerName})
                      </option>
                    ))}
                  </Select>
                )}
              </div>
            </div>

            {/* Instruction Prompt */}
            <div>
              <label htmlFor="task-instruction" className="block text-xs font-semibold text-foreground mb-1.5">
                Instructions / Goal
              </label>
              <Textarea
                id="task-instruction"
                placeholder="Describe the task, expected behavior, files to modify, or specific test cases to verify..."
                rows={5}
                value={instruction}
                onChange={(e) => setInstruction(e.target.value)}
                required
              />
            </div>

            {/* Max Budget */}
            <div>
              <label htmlFor="task-budget" className="block text-xs font-semibold text-foreground mb-1.5">
                Max Budget Limit (USD, optional)
              </label>
              <Input
                id="task-budget"
                type="number"
                step="0.50"
                min="0.50"
                max="100.00"
                value={maxBudgetUsd}
                onChange={(e) => setMaxBudgetUsd(e.target.value)}
                placeholder="5.00"
              />
            </div>
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
              disabled={isSubmitting}
            >
              Cancel
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? "Dispatching..." : "Launch Task"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
