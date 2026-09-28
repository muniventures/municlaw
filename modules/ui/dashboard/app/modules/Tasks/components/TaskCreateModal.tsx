import * as React from "react";
import { AlertTriangle, Shield, ShieldAlert } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Select } from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";
import type {
  HarnessType,
  CreateTaskRequest,
  Project,
  ProviderCredentialReference,
} from "@/api/types";
import { createTask, type CreateTaskPayload } from "@/api/tasks";
import { getAccessibleCredentials } from "@/api/connections";

interface TaskCreateModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  organizationId: string;
  userId: string;
  projects: Project[];
  credentials?: ProviderCredentialReference[];
  onTaskCreated: (taskId: string) => void;
}

const SUPPORTED_MODELS = [
  { id: "claude-3-7-sonnet", name: "Anthropic Claude 3.7 Sonnet (Default)" },
  { id: "claude-3-5-sonnet", name: "Anthropic Claude 3.5 Sonnet" },
  { id: "gpt-4o", name: "OpenAI GPT-4o" },
  { id: "gpt-4.5-preview", name: "OpenAI GPT-4.5 Preview" },
  { id: "deepseek-chat", name: "DeepSeek Chat (V3 / R1)" },
];

const CLAUDE_CODE_MODELS = [
  { id: "claude-3-7-sonnet", name: "Anthropic Claude 3.7 Sonnet (Recommended)" },
  { id: "claude-3-5-sonnet", name: "Anthropic Claude 3.5 Sonnet" },
];

function isModelAllowed(
  selectedModel: string,
  credential?: ProviderCredentialReference
): { allowed: boolean; reason?: string } {
  if (!credential || credential.scope !== "Organization" || !credential.policy) {
    return { allowed: true };
  }

  const allowedModels = credential.policy.allowedModels;
  if (!allowedModels || allowedModels.length === 0 || allowedModels.includes("*")) {
    return { allowed: true };
  }

  const match = allowedModels.some((allowed) => {
    if (allowed === "*") return true;
    if (allowed.endsWith("*")) {
      const prefix = allowed.slice(0, -1);
      return selectedModel.startsWith(prefix);
    }
    return allowed.toLowerCase() === selectedModel.toLowerCase();
  });

  if (!match) {
    return {
      allowed: false,
      reason: `Selected model '${selectedModel}' is not allowed by this organization key's policy. Allowed models: ${allowedModels.join(", ")}`,
    };
  }

  return { allowed: true };
}

function isBudgetCapExceeded(
  credential?: ProviderCredentialReference
): { exceeded: boolean; reason?: string } {
  if (!credential || credential.scope !== "Organization" || !credential.policy) {
    return { exceeded: false };
  }
  const { monthlySpendLimitUsd, currentSpendUsd } = credential.policy;
  if (monthlySpendLimitUsd != null && monthlySpendLimitUsd > 0) {
    if (currentSpendUsd >= monthlySpendLimitUsd) {
      return {
        exceeded: true,
        reason: `Monthly spend limit ($${monthlySpendLimitUsd.toFixed(2)}) has been reached for this organization key ($${currentSpendUsd.toFixed(2)} spent).`,
      };
    }
  }
  return { exceeded: false };
}

export function TaskCreateModal({
  open,
  onOpenChange,
  organizationId,
  userId,
  projects,
  credentials = [],
  onTaskCreated,
}: TaskCreateModalProps) {
  const [projectId, setProjectId] = React.useState(projects[0]?.id || "");
  const [title, setTitle] = React.useState("");
  const [baseBranch, setBaseBranch] = React.useState(
    projects[0]?.defaultBaseBranch || "main"
  );
  const [harnessType, setHarnessType] = React.useState<HarnessType>("OpenCode");
  const [model, setModel] = React.useState(SUPPORTED_MODELS[0].id);
  const [credentialsList, setCredentialsList] = React.useState<ProviderCredentialReference[]>(
    credentials
  );
  const [credentialId, setCredentialId] = React.useState(
    credentials[0]?.id || ""
  );
  const [instruction, setInstruction] = React.useState("");
  const [maxBudgetUsd, setMaxBudgetUsd] = React.useState<string>("5.00");
  const [isSubmitting, setIsSubmitting] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  const handleHarnessChange = (newHarness: HarnessType) => {
    setHarnessType(newHarness);
    if (newHarness === "ClaudeCode") {
      if (model !== "claude-3-7-sonnet" && model !== "claude-3-5-sonnet") {
        setModel("claude-3-7-sonnet");
      }
    }
  };

  const availableModels = harnessType === "ClaudeCode" ? CLAUDE_CODE_MODELS : SUPPORTED_MODELS;

  // Sync projects
  React.useEffect(() => {
    if (projects.length > 0 && !projectId) {
      setProjectId(projects[0].id);
      setBaseBranch(projects[0].defaultBaseBranch || "main");
    }
  }, [projects, projectId]);

  // Sync prop credentials if provided
  React.useEffect(() => {
    if (credentials.length > 0) {
      setCredentialsList(credentials);
    }
  }, [credentials]);

  // Fetch accessible credentials whenever modal opens or organizationId changes
  React.useEffect(() => {
    if (!open) return;
    let isCancelled = false;

    getAccessibleCredentials(organizationId)
      .then((accessible) => {
        if (!isCancelled && accessible && accessible.length > 0) {
          setCredentialsList(accessible);
        }
      })
      .catch(() => {
        // Keep existing credentialsList if accessible fetch fails
      });

    return () => {
      isCancelled = true;
    };
  }, [open, organizationId]);

  // Ensure an active credential is selected
  React.useEffect(() => {
    if (credentialsList.length > 0) {
      if (!credentialId || !credentialsList.some((c) => c.id === credentialId)) {
        setCredentialId(credentialsList[0].id);
      }
    }
  }, [credentialsList, credentialId]);

  const selectedCred = credentialsList.find((c) => c.id === credentialId);
  const modelCheck = isModelAllowed(model, selectedCred);
  const budgetCheck = isBudgetCapExceeded(selectedCred);

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
    if (!modelCheck.allowed) {
      setError(modelCheck.reason || "Selected model is not permitted by this organization key.");
      return;
    }
    if (budgetCheck.exceeded) {
      setError(budgetCheck.reason || "This organization key has reached its monthly spend limit.");
      return;
    }

    try {
      setIsSubmitting(true);
      setError(null);

      const payload: CreateTaskRequest = {
        projectId,
        userId,
        title: title.trim(),
        baseBranch: baseBranch.trim() || "main",
        providerCredentialReferenceId: credentialId,
        model,
        instruction: instruction.trim(),
        harnessVersion: harnessType === "ClaudeCode" ? "claude-code" : "opencode-v1",
        harnessType,
        maxBudgetUsd: maxBudgetUsd ? parseFloat(maxBudgetUsd) : undefined,
      };

      const result = await createTask(organizationId, payload as unknown as CreateTaskPayload);
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

            {/* Harness & Model */}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
              <div>
                <label htmlFor="task-harness" className="block text-xs font-semibold text-foreground mb-1.5">
                  AI Coding Harness
                </label>
                <Select
                  id="task-harness"
                  value={harnessType}
                  onChange={(e) => handleHarnessChange(e.target.value as HarnessType)}
                >
                  <option value="OpenCode">OpenCode (v1.18.32)</option>
                  <option value="ClaudeCode">Claude Code</option>
                </Select>
              </div>

              <div>
                <label htmlFor="task-model" className="block text-xs font-semibold text-foreground mb-1.5">
                  AI Model
                </label>
                <Select
                  id="task-model"
                  value={model}
                  onChange={(e) => setModel(e.target.value)}
                >
                  {availableModels.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.name}
                    </option>
                  ))}
                </Select>
              </div>
            </div>

            {/* Provider Key (BYOK) */}
            <div>
              <label htmlFor="task-credential" className="block text-xs font-semibold text-foreground mb-1.5">
                Provider Key (BYOK)
              </label>
              {credentialsList.length === 0 ? (
                <div className="text-xs text-amber-600 bg-amber-500/10 p-2 rounded border border-amber-500/20">
                  No keys registered. Go to Connections to add your write-only key.
                </div>
              ) : (
                <Select
                  id="task-credential"
                  value={credentialId}
                  onChange={(e) => setCredentialId(e.target.value)}
                >
                  {credentialsList.map((c) => {
                    const isOrg = c.scope === "Organization";
                    const prefix = isOrg ? "[Org Shared]" : "[Personal]";
                    return (
                      <option key={c.id} value={c.id}>
                        {prefix} {c.label} ({c.providerName})
                      </option>
                    );
                  })}
                </Select>
              )}
            </div>

            {/* Selected Credential Policy info & Badges */}
            {selectedCred && (
              <div className="p-2.5 rounded-lg bg-muted/40 border border-border flex flex-wrap items-center justify-between gap-2 text-xs">
                <div className="flex items-center gap-2">
                  <span className="text-muted-foreground font-medium">Selected Credential:</span>
                  <Badge
                    variant={selectedCred.scope === "Organization" ? "info" : "secondary"}
                    className="text-[10px]"
                  >
                    {selectedCred.scope === "Organization" ? "[Org Shared]" : "[Personal]"}
                  </Badge>
                  {selectedCred.scope === "Organization" && selectedCred.policy?.adminOnly && (
                    <Badge variant="warning" className="text-[10px] gap-1">
                      <ShieldAlert className="h-3 w-3" /> Admin Only
                    </Badge>
                  )}
                  {selectedCred.scope === "Organization" && !selectedCred.policy?.adminOnly && (
                    <Badge variant="secondary" className="text-[10px] gap-1">
                      <Shield className="h-3 w-3 text-emerald-600" /> All Members
                    </Badge>
                  )}
                </div>

                {selectedCred.scope === "Organization" && selectedCred.policy && (
                  <div className="text-[11px] text-muted-foreground font-mono">
                    Spend: ${selectedCred.policy.currentSpendUsd.toFixed(2)}
                    {selectedCred.policy.monthlySpendLimitUsd != null &&
                      ` / $${selectedCred.policy.monthlySpendLimitUsd.toFixed(2)}`}
                  </div>
                )}
              </div>
            )}

            {/* Model Policy Validation Warning */}
            {!modelCheck.allowed && (
              <div className="p-3 rounded-lg bg-amber-500/10 border border-amber-500/20 text-amber-700 dark:text-amber-400 text-xs flex items-start gap-2">
                <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
                <div>
                  <p className="font-semibold">Model Policy Restriction</p>
                  <p className="mt-0.5">{modelCheck.reason}</p>
                </div>
              </div>
            )}

            {/* Budget Cap Warning */}
            {budgetCheck.exceeded && (
              <div className="p-3 rounded-lg bg-destructive/10 border border-destructive/20 text-destructive text-xs flex items-start gap-2">
                <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
                <div>
                  <p className="font-semibold">Budget Cap Exceeded</p>
                  <p className="mt-0.5">{budgetCheck.reason}</p>
                </div>
              </div>
            )}

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
            <Button
              type="submit"
              disabled={isSubmitting || !modelCheck.allowed || budgetCheck.exceeded}
            >
              {isSubmitting ? "Dispatching..." : "Launch Task"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
