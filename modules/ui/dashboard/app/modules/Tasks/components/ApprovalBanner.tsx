import * as React from "react";
import { ShieldAlert, Check, X, AlertTriangle, Hash } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import type { ApprovalRequestDto, ApprovalDecisionType } from "@/api/types";
import { submitApprovalDecision } from "@/api/settings";

interface ApprovalBannerProps {
  approval: ApprovalRequestDto;
  userId: string;
  onDecisionSubmitted: () => void;
}

export function ApprovalBanner({
  approval,
  userId,
  onDecisionSubmitted,
}: ApprovalBannerProps) {
  const [isSubmitting, setIsSubmitting] = React.useState(false);
  const [staleError, setStaleError] = React.useState<string | null>(null);

  const handleDecision = async (decision: ApprovalDecisionType) => {
    try {
      setIsSubmitting(true);
      setStaleError(null);

      await submitApprovalDecision(
        approval.organizationId,
        approval.approvalRequestId,
        {
          userId,
          decision,
          contentVersionHash: approval.contentVersionHash,
        }
      );

      onDecisionSubmitted();
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : "Failed to submit decision";
      // Check for stale approval rejection
      if (msg.includes("invalid_decision") || msg.includes("hash") || msg.includes("mismatch") || msg.includes("expired")) {
        setStaleError(
          "Stale approval rejected: Content or execution state changed after this approval was requested. Current state requires review."
        );
      } else {
        setStaleError(msg);
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="rounded-xl border border-amber-500/40 bg-amber-500/10 p-4 md:p-5 shadow-sm space-y-3">
      <div className="flex flex-col sm:flex-row sm:items-start justify-between gap-3">
        <div className="space-y-1.5 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <ShieldAlert className="h-5 w-5 text-amber-600 shrink-0" />
            <h4 className="font-bold text-sm text-foreground">
              Approval Requested by Agent
            </h4>
            <Badge variant="warning" className="text-xs font-mono">
              {approval.capability}
            </Badge>
          </div>

          <p className="text-xs text-foreground font-medium pl-7">
            {approval.actionDescription}
          </p>

          <div className="flex items-center gap-1.5 text-[11px] text-muted-foreground pl-7 font-mono">
            <Hash className="h-3 w-3" />
            <span>Content Version Hash:</span>
            <span className="font-semibold text-foreground">
              {approval.contentVersionHash.slice(0, 16)}...
            </span>
          </div>
        </div>

        <div className="flex items-center gap-2 self-end sm:self-start shrink-0">
          <Button
            size="sm"
            variant="outline"
            className="text-xs gap-1 border-destructive/40 text-destructive hover:bg-destructive/10"
            disabled={isSubmitting}
            onClick={() => handleDecision("Deny")}
          >
            <X className="h-3.5 w-3.5" />
            Deny
          </Button>

          <Button
            size="sm"
            className="text-xs gap-1 bg-emerald-600 hover:bg-emerald-700 text-white"
            disabled={isSubmitting}
            onClick={() => handleDecision("AllowOnce")}
          >
            <Check className="h-3.5 w-3.5" />
            Allow Once
          </Button>
        </div>
      </div>

      {staleError && (
        <div className="flex items-center gap-2 p-2.5 rounded-lg bg-destructive/15 border border-destructive/30 text-destructive text-xs font-medium">
          <AlertTriangle className="h-4 w-4 shrink-0" />
          <span>{staleError}</span>
        </div>
      )}
    </div>
  );
}
