import * as React from "react";
import { useParams, Link } from "react-router";
import {
  ArrowLeft,
  Ban,
  GitBranch,
  GitCommit,
  GitPullRequest,
  Clock,
  PlayCircle,
  CheckCircle2,
  XCircle,
  AlertCircle,
  Cpu,
  Layers,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import { ActivityLog } from "./components/ActivityLog";
import { DiffView } from "./components/DiffView";
import { CheckResults } from "./components/CheckResults";
import { ApprovalBanner } from "./components/ApprovalBanner";
import { DeliveryModal } from "./components/DeliveryModal";
import { UsageCard } from "./components/UsageCard";
import { FollowUpInput } from "./components/FollowUpInput";
import { PreviewDeploymentCard } from "./components/PreviewDeploymentCard";

import {
  getTask,
  cancelRun,
  createFollowUp,
  getDelivery,
  getTaskQueueStatus,
} from "@/api/tasks";
import { sseManager, type SseConnectionStatus } from "@/api/sse";
import type {
  TaskEntity,
  TaskRun,
  TaskQueueStatus,
  TaskEventDto,
  DiffFileChange,
  CheckResultPayload,
  ApprovalRequestDto,
  DeliveryRecord,
  UsageRecordEntity,
} from "@/api/types";
import { getRuntimeConfig } from "@/core/config/runtime";

export function TaskDetailModule() {
  const { taskId } = useParams<{ taskId: string }>();
  const [config] = React.useState(() => getRuntimeConfig());

  const [task, setTask] = React.useState<TaskEntity | null>(null);
  const [activeRunId, setActiveRunId] = React.useState<string | null>(null);
  const [activeTab, setActiveTab] = React.useState<string>("stream");
  const [isLoading, setIsLoading] = React.useState(true);
  const [error, setError] = React.useState<string | null>(null);

  // Real-time state
  const [events, setEvents] = React.useState<TaskEventDto[]>([]);
  const [sseStatus, setSseStatus] = React.useState<SseConnectionStatus>("idle");
  const [diffFiles, setDiffFiles] = React.useState<DiffFileChange[]>([]);
  const [baseCommitSha, setBaseCommitSha] = React.useState<string>("");
  const [reviewedCommitSha, setReviewedCommitSha] = React.useState<string>("");
  const [checks, setChecks] = React.useState<CheckResultPayload[]>([]);
  const [pendingApprovals, setPendingApprovals] = React.useState<ApprovalRequestDto[]>([]);
  const [delivery, setDelivery] = React.useState<DeliveryRecord | null>(null);
  const [deliveryModalOpen, setDeliveryModalOpen] = React.useState(false);
  const [usage, setUsage] = React.useState<UsageRecordEntity | null>(null);
  const [queueStatus, setQueueStatus] = React.useState<TaskQueueStatus | null>(null);
  const [isCancelling, setIsCancelling] = React.useState(false);

  // Load Task details
  const fetchTaskDetails = React.useCallback(async () => {
    if (!taskId) return;
    try {
      setIsLoading(true);
      setError(null);
      const data = await getTask(config.defaultOrganizationId, taskId);
      setTask(data);

      const latestRun = data.runs?.[data.runs.length - 1];
      if (latestRun) {
        setActiveRunId(latestRun.id);
        setBaseCommitSha(data.baseCommitSha || "");
      }

      // Check delivery record
      const del = await getDelivery(config.defaultOrganizationId, taskId);
      setDelivery(del);

      // Check queue status
      try {
        const qStatus = await getTaskQueueStatus(taskId);
        setQueueStatus(qStatus);
      } catch {
        // Queue endpoint may not be available or returns 404
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to load task details");
    } finally {
      setIsLoading(false);
    }
  }, [taskId, config.defaultOrganizationId]);

  React.useEffect(() => {
    fetchTaskDetails();
  }, [fetchTaskDetails]);

  // Setup SSE stream subscription for the active run
  React.useEffect(() => {
    if (!taskId || !activeRunId) return;

    // Reset run-specific collections when switching runs
    setEvents([]);
    setDiffFiles([]);
    setChecks([]);
    setPendingApprovals([]);

    const unsubscribe = sseManager.subscribe(
      config.defaultOrganizationId,
      taskId,
      activeRunId,
      {
        onEvent: (evt: TaskEventDto) => {
          setEvents((prev) => {
            // Deduplicate
            if (prev.some((e) => e.eventId === evt.eventId)) return prev;
            return [...prev, evt];
          });

          // Handle specific events to update UI projections
          try {
            const payload = JSON.parse(evt.payloadJson);

            if (evt.eventType === "DiffUpdated") {
              if (Array.isArray(payload.files)) {
                setDiffFiles(payload.files);
              }
              if (payload.baseCommitSha) setBaseCommitSha(payload.baseCommitSha);
              if (payload.headCommitSha) setReviewedCommitSha(payload.headCommitSha);
            } else if (evt.eventType === "CheckExecuted") {
              setChecks((prev) => [
                ...prev,
                {
                  command: payload.command || "",
                  exitCode: payload.exitCode ?? 0,
                  output: payload.output || "",
                  passed: Boolean(payload.passed),
                  durationMs: payload.durationMs ?? 0,
                },
              ]);
            } else if (evt.eventType === "ApprovalRequested") {
              setPendingApprovals((prev) => {
                if (prev.some((a) => a.approvalRequestId === payload.approvalRequestId)) {
                  return prev;
                }
                return [
                  ...prev,
                  {
                    approvalRequestId: payload.approvalRequestId,
                    organizationId: config.defaultOrganizationId,
                    taskId,
                    runId: activeRunId,
                    capability: payload.capability,
                    actionDescription: payload.actionDescription,
                    contentVersionHash: payload.contentVersionHash,
                    createdAt: evt.timestamp,
                  },
                ];
              });
            } else if (evt.eventType === "ApprovalResolved") {
              setPendingApprovals((prev) =>
                prev.filter((a) => a.approvalRequestId !== payload.approvalRequestId)
              );
            }
          } catch {
            // Ignore non-json payload
          }
        },
        onStatusChange: (status) => {
          setSseStatus(status);
        },
      }
    );

    return () => {
      unsubscribe();
    };
  }, [taskId, activeRunId, config.defaultOrganizationId]);

  const activeRun = task?.runs?.find((r) => r.id === activeRunId) || task?.runs?.[task.runs.length - 1];

  const handleCancelRun = async () => {
    if (!taskId || !activeRun) return;
    if (!confirm("Are you sure you want to cancel the current task execution?")) return;

    try {
      setIsCancelling(true);
      await cancelRun(config.defaultOrganizationId, taskId, activeRun.id, {
        userId: config.defaultUserId,
        reason: "User cancelled from console",
      });
      await fetchTaskDetails();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Failed to cancel run");
    } finally {
      setIsCancelling(false);
    }
  };

  const handleSendFollowUp = async (instruction: string) => {
    if (!taskId) return;
    const newRun = await createFollowUp(config.defaultOrganizationId, taskId, {
      userId: config.defaultUserId,
      instruction,
    });
    await fetchTaskDetails();
    setActiveRunId(newRun.id);
  };

  const isTerminalStatus =
    activeRun?.status === "Completed" ||
    activeRun?.status === "Failed" ||
    activeRun?.status === "Cancelled";

  if (isLoading) {
    return (
      <div className="p-12 text-center text-muted-foreground font-mono text-sm">
        Loading task workspace...
      </div>
    );
  }

  if (error || !task) {
    return (
      <div className="p-8 space-y-4">
        <Link to="/tasks" className="inline-flex items-center gap-1.5 text-xs text-primary font-medium hover:underline">
          <ArrowLeft className="h-3.5 w-3.5" /> Back to Tasks
        </Link>
        <div className="p-4 rounded-xl bg-destructive/10 border border-destructive/20 text-destructive text-sm">
          {error || "Task not found"}
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Top back navigation and Actions */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div className="space-y-1">
          <div className="flex items-center gap-2">
            <Link
              to="/tasks"
              className="text-xs font-semibold text-muted-foreground hover:text-foreground flex items-center gap-1"
            >
              <ArrowLeft className="h-3.5 w-3.5" /> Tasks
            </Link>
            <span className="text-muted-foreground">/</span>
            <span className="text-xs font-mono text-muted-foreground">
              {task.id.slice(0, 8)}
            </span>
          </div>

          <h1 className="text-xl md:text-2xl font-bold tracking-tight text-foreground">
            {task.title}
          </h1>

          <div className="flex items-center gap-3 text-xs font-mono text-muted-foreground flex-wrap pt-0.5">
            <span className="flex items-center gap-1 text-foreground font-semibold">
              <GitBranch className="h-3.5 w-3.5 text-primary" />
              {task.taskBranch}
            </span>
            <span>Base: {task.baseBranch}</span>
            {activeRun && (
              <span className="px-2 py-0.5 rounded bg-muted text-foreground">
                Run #{activeRun.runIndex} ({activeRun.resolvedModel})
              </span>
            )}
          </div>
        </div>

        {/* Action Controls */}
        <div className="flex items-center gap-2 self-start sm:self-center shrink-0">
          {!isTerminalStatus && (
            <Button
              variant="outline"
              size="sm"
              disabled={isCancelling || activeRun?.status === "Cancelling"}
              onClick={handleCancelRun}
              className="text-xs text-destructive border-destructive/30 hover:bg-destructive/10 gap-1.5"
            >
              <Ban className="h-3.5 w-3.5" />
              <span>{isCancelling ? "Cancelling..." : "Cancel Run"}</span>
            </Button>
          )}

          <Button
            size="sm"
            onClick={() => setDeliveryModalOpen(true)}
            className="text-xs gap-1.5"
          >
            <GitPullRequest className="h-3.5 w-3.5" />
            <span>{delivery ? "View Delivery" : "Publish Draft PR"}</span>
          </Button>
        </div>
      </div>

      {/* Queued Status Banner */}
      {(activeRun?.status?.toLowerCase() === "queued" || (!activeRun && task.runs?.length === 0)) && (
        <div className="rounded-xl border border-amber-500/30 bg-amber-500/10 p-4 text-amber-900 dark:text-amber-200 flex items-start sm:items-center justify-between gap-3 shadow-xs">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg bg-amber-500/20 text-amber-600 dark:text-amber-400 shrink-0">
              <Clock className="h-5 w-5 animate-pulse" />
            </div>
            <div>
              <h3 className="font-semibold text-sm text-foreground">
                Waiting in Queue — Position #{activeRun?.queuePosition ?? task.queuePosition ?? queueStatus?.queuePosition ?? 1}. Execution will automatically start when an active task slot becomes available.
              </h3>
              <p className="text-xs text-muted-foreground mt-0.5">
                Your task is queued in fair FIFO order. As soon as an active execution finishes, this task will begin preparing automatically.
              </p>
            </div>
          </div>
          <Badge
            variant="warning"
            className="shrink-0 bg-amber-500/20 text-amber-700 dark:text-amber-400 border border-amber-500/30"
          >
            Position #{activeRun?.queuePosition ?? task.queuePosition ?? queueStatus?.queuePosition ?? 1}
          </Badge>
        </div>
      )}

      {/* Pending Approvals Banner */}
      {pendingApprovals.map((approval) => (
        <ApprovalBanner
          key={approval.approvalRequestId}
          approval={approval}
          userId={config.defaultUserId}
          onDecisionSubmitted={() => {
            setPendingApprovals((prev) =>
              prev.filter((a) => a.approvalRequestId !== approval.approvalRequestId)
            );
            fetchTaskDetails();
          }}
        />
      ))}

      {/* Run Selector (if multi-run / follow-ups exist) */}
      {task.runs && task.runs.length > 1 && (
        <div className="flex items-center gap-2 overflow-x-auto pb-1 text-xs">
          <span className="font-semibold text-muted-foreground flex items-center gap-1 shrink-0">
            <Layers className="h-3.5 w-3.5" /> Execution Turns:
          </span>
          {task.runs.map((r) => (
            <button
              key={r.id}
              onClick={() => setActiveRunId(r.id)}
              className={`px-2.5 py-1 rounded-md font-mono shrink-0 transition-colors cursor-pointer ${
                activeRunId === r.id
                  ? "bg-primary text-primary-foreground font-semibold shadow-xs"
                  : "bg-muted hover:bg-muted/80 text-muted-foreground hover:text-foreground"
              }`}
            >
              Run #{r.runIndex} ({r.status})
            </button>
          ))}
        </div>
      )}

      {/* Preview Deployment Environment */}
      <PreviewDeploymentCard
        taskId={task.id}
        branchName={task.taskBranch}
      />

      {/* Main Tabs Layout */}
      <Tabs value={activeTab} onValueChange={setActiveTab}>
        <TabsList className="mb-4">
          <TabsTrigger value="stream" className="gap-1.5">
            <PlayCircle className="h-3.5 w-3.5" />
            Activity Log
          </TabsTrigger>
          <TabsTrigger value="diff" className="gap-1.5">
            <GitCommit className="h-3.5 w-3.5" />
            Changed Files ({diffFiles.length})
          </TabsTrigger>
          <TabsTrigger value="checks" className="gap-1.5">
            <CheckCircle2 className="h-3.5 w-3.5" />
            Checks ({checks.length})
          </TabsTrigger>
          <TabsTrigger value="usage" className="gap-1.5">
            <Cpu className="h-3.5 w-3.5" />
            Resource Usage
          </TabsTrigger>
        </TabsList>

        <TabsContent value="stream" className="space-y-4">
          <ActivityLog
            events={events}
            sseStatus={sseStatus}
            onRefresh={fetchTaskDetails}
          />
          <FollowUpInput
            onSendFollowUp={handleSendFollowUp}
            disabled={!isTerminalStatus && activeRun?.status !== "AwaitingInput"}
          />
        </TabsContent>

        <TabsContent value="diff">
          <DiffView
            baseCommitSha={baseCommitSha}
            reviewedCommitSha={reviewedCommitSha}
            files={diffFiles}
          />
        </TabsContent>

        <TabsContent value="checks">
          <CheckResults checks={checks} />
        </TabsContent>

        <TabsContent value="usage">
          <UsageCard
            usage={usage}
            retentionDays={7}
            activeRuns={queueStatus?.activeRuns}
            maxConcurrentRuns={queueStatus?.maxConcurrentRuns}
          />
        </TabsContent>
      </Tabs>

      {/* Delivery Publication Modal */}
      {activeRun && (
        <DeliveryModal
          open={deliveryModalOpen}
          onOpenChange={setDeliveryModalOpen}
          organizationId={config.defaultOrganizationId}
          taskId={task.id}
          runId={activeRun.id}
          userId={config.defaultUserId}
          baseCommitSha={baseCommitSha}
          reviewedCommitSha={reviewedCommitSha}
          defaultTargetBranch={task.baseBranch}
          defaultTitle={task.title}
          existingDelivery={delivery}
          onDeliveryPublished={(record) => {
            setDelivery(record);
          }}
        />
      )}
    </div>
  );
}
