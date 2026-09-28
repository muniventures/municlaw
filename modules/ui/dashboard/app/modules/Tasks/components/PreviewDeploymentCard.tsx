import * as React from "react";
import {
  Globe,
  ExternalLink,
  Loader2,
  Trash2,
  Rocket,
  AlertTriangle,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { getPreview, triggerPreview, tearDownPreview } from "@/api/preview";
import type { PreviewDeployment } from "@/api/types";

export interface PreviewDeploymentCardProps {
  taskId: string;
  branchName?: string;
  className?: string;
  initialPreview?: PreviewDeployment | null;
  onPreviewChange?: (preview: PreviewDeployment | null) => void;
}

export function PreviewDeploymentCard({
  taskId,
  branchName,
  className = "",
  initialPreview,
  onPreviewChange,
}: PreviewDeploymentCardProps) {
  const [preview, setPreview] = React.useState<PreviewDeployment | null>(
    initialPreview ?? null
  );
  const [isLoading, setIsLoading] = React.useState<boolean>(initialPreview === undefined);
  const [isActionLoading, setIsActionLoading] = React.useState<boolean>(false);
  const [actionError, setActionError] = React.useState<string | null>(null);

  const fetchPreview = React.useCallback(async () => {
    if (!taskId) return;
    try {
      const data = await getPreview(taskId);
      setPreview(data);
      onPreviewChange?.(data);
    } catch (err: unknown) {
      // Do not surface standard 404s as errors
      const message = err instanceof Error ? err.message : "Failed to load preview";
      setActionError(message);
    } finally {
      setIsLoading(false);
    }
  }, [taskId, onPreviewChange]);

  React.useEffect(() => {
    if (initialPreview !== undefined) {
      setPreview(initialPreview);
      setIsLoading(false);
      return;
    }
    fetchPreview();
  }, [fetchPreview, initialPreview]);

  // Polling while deploying
  React.useEffect(() => {
    if (preview?.status !== "Deploying") return;

    const intervalId = setInterval(async () => {
      try {
        const latest = await getPreview(taskId);
        if (latest) {
          setPreview(latest);
          onPreviewChange?.(latest);
        }
      } catch {
        // Polling failure, will retry next tick
      }
    }, 3000);

    return () => clearInterval(intervalId);
  }, [preview?.status, taskId, onPreviewChange]);

  const handleDeploy = async () => {
    if (!taskId || isActionLoading) return;
    try {
      setIsActionLoading(true);
      setActionError(null);
      const res = await triggerPreview(taskId);
      setPreview(res);
      onPreviewChange?.(res);
    } catch (err: unknown) {
      setActionError(
        err instanceof Error ? err.message : "Failed to trigger preview deployment"
      );
    } finally {
      setIsActionLoading(false);
    }
  };

  const handleTearDown = async () => {
    if (!taskId || isActionLoading) return;
    if (
      !window.confirm(
        "Are you sure you want to tear down this preview deployment? The ephemeral environment will be decommissioned."
      )
    ) {
      return;
    }

    try {
      setIsActionLoading(true);
      setActionError(null);
      const res = await tearDownPreview(taskId);
      setPreview(res);
      onPreviewChange?.(res);
    } catch (err: unknown) {
      setActionError(
        err instanceof Error ? err.message : "Failed to tear down preview deployment"
      );
    } finally {
      setIsActionLoading(false);
    }
  };

  const effectiveBranch = preview?.branchName || branchName;
  const isDeploying = preview?.status === "Deploying";
  const isActive = preview?.status === "Active";
  const isFailed = preview?.status === "Failed";
  const isNoPreviewOrTornDown =
    !preview || preview.status === "None" || preview.status === "TornDown";

  return (
    <div
      className={`rounded-xl border border-border bg-card p-4 space-y-3 shadow-xs ${className}`}
    >
      {/* Card Header */}
      <div className="flex items-center justify-between border-b border-border pb-2.5">
        <div className="flex items-center gap-2">
          <Globe className="h-4 w-4 text-primary" />
          <h3 className="text-xs font-semibold text-foreground uppercase tracking-wider">
            Preview Deployment
          </h3>
        </div>

        {/* Status Pills */}
        {isActive && (
          <Badge variant="success" className="gap-1">
            Live Preview
          </Badge>
        )}
        {isDeploying && (
          <Badge variant="warning" className="gap-1 animate-pulse">
            <Loader2 className="h-3 w-3 animate-spin" />
            Deploying
          </Badge>
        )}
        {isFailed && (
          <Badge variant="destructive" className="gap-1">
            <AlertTriangle className="h-3 w-3" />
            Failed
          </Badge>
        )}
        {isLoading && (
          <Loader2 className="h-3.5 w-3.5 animate-spin text-muted-foreground" />
        )}
      </div>

      {/* Action Error Alert */}
      {actionError && (
        <div className="p-2.5 rounded-lg bg-destructive/10 border border-destructive/20 text-destructive text-xs flex items-center justify-between gap-2">
          <div className="flex items-center gap-2">
            <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
            <span>{actionError}</span>
          </div>
          <button
            onClick={() => setActionError(null)}
            className="text-[11px] font-semibold underline hover:opacity-80 cursor-pointer"
          >
            Dismiss
          </button>
        </div>
      )}

      {/* Card Body by State */}
      {isDeploying ? (
        /* When Deploying */
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3 rounded-lg bg-amber-500/10 border border-amber-500/20">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg bg-amber-500/20 text-amber-600 dark:text-amber-400 shrink-0">
              <Loader2 className="h-5 w-5 animate-spin" />
            </div>
            <div className="space-y-0.5">
              <p className="text-xs font-medium text-foreground">
                Provisioning preview environment on Minicloud branch infrastructure...
              </p>
              {effectiveBranch && (
                <p className="text-[11px] text-muted-foreground font-mono">
                  Branch: {effectiveBranch}
                </p>
              )}
            </div>
          </div>
        </div>
      ) : isActive ? (
        /* When Active */
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3 rounded-lg bg-emerald-500/10 border border-emerald-500/20">
          <div className="space-y-1">
            <div className="flex items-center gap-2 flex-wrap">
              <Badge variant="success" className="gap-1">
                Live Preview
              </Badge>
              {effectiveBranch && (
                <span className="text-xs font-mono text-muted-foreground">
                  Branch: <span className="font-semibold text-foreground">{effectiveBranch}</span>
                </span>
              )}
            </div>

            {preview?.previewUrl ? (
              <a
                href={preview.previewUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-1.5 text-xs font-mono font-medium text-primary hover:underline break-all"
              >
                <span>{preview.previewUrl}</span>
                <ExternalLink className="h-3.5 w-3.5 shrink-0" />
              </a>
            ) : (
              <span className="text-xs font-mono text-muted-foreground">
                URL pending assignment
              </span>
            )}
          </div>

          <Button
            variant="outline"
            size="sm"
            disabled={isActionLoading}
            onClick={handleTearDown}
            className="text-xs text-destructive border-destructive/30 hover:bg-destructive/10 gap-1.5 shrink-0 self-start sm:self-center"
          >
            {isActionLoading ? (
              <Loader2 className="h-3.5 w-3.5 animate-spin" />
            ) : (
              <Trash2 className="h-3.5 w-3.5" />
            )}
            <span>Tear Down</span>
          </Button>
        </div>
      ) : isFailed ? (
        /* When Failed */
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3 rounded-lg bg-destructive/10 border border-destructive/20">
          <div className="flex items-center gap-3">
            <div className="p-2 rounded-lg bg-destructive/20 text-destructive shrink-0">
              <AlertTriangle className="h-5 w-5" />
            </div>
            <div className="space-y-0.5">
              <div className="flex items-center gap-2">
                <Badge variant="destructive" className="gap-1">
                  Failed
                </Badge>
                {effectiveBranch && (
                  <span className="text-xs font-mono text-muted-foreground">
                    Branch: {effectiveBranch}
                  </span>
                )}
              </div>
              <p className="text-xs text-muted-foreground font-mono">
                {preview?.errorMessage || "Preview deployment failed on branch infrastructure"}
              </p>
            </div>
          </div>

          <Button
            size="sm"
            disabled={isActionLoading}
            onClick={handleDeploy}
            className="text-xs gap-1.5 shrink-0 self-start sm:self-center"
          >
            {isActionLoading ? (
              <Loader2 className="h-3.5 w-3.5 animate-spin" />
            ) : (
              <Rocket className="h-3.5 w-3.5" />
            )}
            <span>Retry Deploy</span>
          </Button>
        </div>
      ) : isNoPreviewOrTornDown ? (
        /* When no preview (or TornDown) */
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 p-3 rounded-lg bg-muted/40 border border-border/60">
          <div className="space-y-0.5">
            <p className="text-xs text-muted-foreground">
              Deploy an ephemeral preview environment for this task branch to test and inspect changes in real-time.
            </p>
            {effectiveBranch && (
              <p className="text-[11px] font-mono text-muted-foreground">
                Target Branch: <span className="font-semibold text-foreground">{effectiveBranch}</span>
                {preview?.status === "TornDown" && " (Torn Down)"}
              </p>
            )}
          </div>

          <Button
            size="sm"
            disabled={isActionLoading}
            onClick={handleDeploy}
            className="text-xs gap-1.5 shrink-0 self-start sm:self-center"
          >
            {isActionLoading ? (
              <Loader2 className="h-3.5 w-3.5 animate-spin" />
            ) : (
              <Rocket className="h-3.5 w-3.5" />
            )}
            <span>Deploy Preview</span>
          </Button>
        </div>
      ) : null}
    </div>
  );
}
