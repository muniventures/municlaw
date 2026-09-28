import * as React from "react";
import { GitPullRequest, ExternalLink, AlertTriangle, CheckCircle2 } from "lucide-react";
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
import type { DeliveryRecord } from "@/api/types";
import { publishDelivery, type PublishDeliveryPayload } from "@/api/tasks";

interface DeliveryModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  organizationId: string;
  taskId: string;
  runId: string;
  userId: string;
  baseCommitSha: string;
  reviewedCommitSha: string;
  defaultTargetBranch?: string;
  defaultTitle?: string;
  existingDelivery?: DeliveryRecord | null;
  onDeliveryPublished: (record: DeliveryRecord) => void;
}

export function DeliveryModal({
  open,
  onOpenChange,
  organizationId,
  taskId,
  runId,
  userId,
  baseCommitSha,
  reviewedCommitSha,
  defaultTargetBranch = "main",
  defaultTitle = "",
  existingDelivery,
  onDeliveryPublished,
}: DeliveryModalProps) {
  const [targetBranch, setTargetBranch] = React.useState(defaultTargetBranch);
  const [title, setTitle] = React.useState(defaultTitle || "MuniClaw PR Draft");
  const [body, setBody] = React.useState(
    "Automated draft delivered from MuniClaw private coding VPS.\nReviewed changes and tests verified."
  );
  const [isSubmitting, setIsSubmitting] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [publishedRecord, setPublishedRecord] = React.useState<DeliveryRecord | null>(
    existingDelivery || null
  );

  React.useEffect(() => {
    if (existingDelivery) {
      setPublishedRecord(existingDelivery);
    }
  }, [existingDelivery]);

  const handlePublish = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!reviewedCommitSha) {
      setError("Cannot publish delivery without a reviewed commit SHA.");
      return;
    }

    try {
      setIsSubmitting(true);
      setError(null);

      const payload: PublishDeliveryPayload = {
        runId,
        userId,
        baseCommitSha,
        reviewedCommitSha,
        targetBranch: targetBranch.trim() || "main",
        title: title.trim(),
        body: body.trim(),
      };

      const record = await publishDelivery(organizationId, taskId, payload);
      setPublishedRecord(record);
      onDeliveryPublished(record);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : "Failed to publish draft";
      if (
        msg.includes("publication_failed") ||
        msg.includes("stale") ||
        msg.includes("mismatch")
      ) {
        setError(
          "Publication rejected: Commit was modified after review or base branch drifted. Please re-review the latest commit before delivering."
        );
      } else {
        setError(msg);
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent onClose={() => onOpenChange(false)} className="max-w-lg">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <GitPullRequest className="h-5 w-5 text-primary" />
            Publish Draft Delivery
          </DialogTitle>
          <DialogDescription>
            Publish verified changes as a Pull Request / Merge Request bound to reviewed commits.
          </DialogDescription>
        </DialogHeader>

        {publishedRecord ? (
          <div className="py-4 space-y-4">
            <div className="p-4 rounded-xl bg-emerald-500/10 border border-emerald-500/20 text-foreground space-y-2">
              <div className="flex items-center gap-2 text-emerald-600 font-semibold text-sm">
                <CheckCircle2 className="h-4 w-4" />
                <span>Draft Published Successfully</span>
              </div>
              <p className="text-xs text-muted-foreground">
                Your draft has been submitted to the upstream repository.
              </p>
              {publishedRecord.remotePrUrl && (
                <div className="pt-2">
                  <a
                    href={publishedRecord.remotePrUrl}
                    target="_blank"
                    rel="noreferrer"
                    className="inline-flex items-center gap-1.5 text-xs font-semibold text-primary underline"
                  >
                    View Remote Pull Request #{publishedRecord.remotePrNumber || "Draft"}
                    <ExternalLink className="h-3 w-3" />
                  </a>
                </div>
              )}
            </div>

            <div className="text-xs font-mono space-y-1 bg-muted/40 p-3 rounded-lg border border-border">
              <div>Target Branch: {publishedRecord.targetBranch}</div>
              <div>Published Commit: {publishedRecord.publishedCommitSha?.slice(0, 10) || "latest"}</div>
            </div>

            <DialogFooter>
              <Button type="button" onClick={() => onOpenChange(false)}>
                Close
              </Button>
            </DialogFooter>
          </div>
        ) : (
          <form onSubmit={handlePublish} className="space-y-4 py-2">
            {error && (
              <div className="p-3 rounded-lg bg-destructive/10 border border-destructive/20 text-destructive text-xs font-medium flex items-center gap-2">
                <AlertTriangle className="h-4 w-4 shrink-0" />
                <span>{error}</span>
              </div>
            )}

            {/* Reviewed Tuple Metadata */}
            <div className="p-3 rounded-lg bg-muted/50 border border-border text-xs space-y-1 font-mono">
              <div className="font-semibold text-foreground font-sans">
                Review Binding Tuple:
              </div>
              <div>Base Commit: {baseCommitSha ? baseCommitSha.slice(0, 10) : "N/A"}</div>
              <div>Reviewed Commit: {reviewedCommitSha ? reviewedCommitSha.slice(0, 10) : "Pending"}</div>
            </div>

            <div>
              <label htmlFor="delivery-target-branch" className="block text-xs font-semibold text-foreground mb-1.5">
                Target Branch
              </label>
              <Input
                id="delivery-target-branch"
                value={targetBranch}
                onChange={(e) => setTargetBranch(e.target.value)}
                placeholder="main"
                required
              />
            </div>

            <div>
              <label htmlFor="delivery-pr-title" className="block text-xs font-semibold text-foreground mb-1.5">
                Pull Request Title
              </label>
              <Input
                id="delivery-pr-title"
                value={title}
                onChange={(e) => setTitle(e.target.value)}
                placeholder="e.g. fix: update retry policy in payment gateway"
                required
              />
            </div>

            <div>
              <label htmlFor="delivery-pr-body" className="block text-xs font-semibold text-foreground mb-1.5">
                Description / Release Summary
              </label>
              <Textarea
                id="delivery-pr-body"
                rows={4}
                value={body}
                onChange={(e) => setBody(e.target.value)}
              />
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
              <Button type="submit" disabled={isSubmitting || !reviewedCommitSha}>
                {isSubmitting ? "Publishing..." : "Submit Delivery"}
              </Button>
            </DialogFooter>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
