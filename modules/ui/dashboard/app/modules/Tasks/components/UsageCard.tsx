import { Coins, HardDrive, Info, Layers } from "lucide-react";
import type { UsageRecordEntity } from "@/api/types";

interface UsageCardProps {
  usage?: UsageRecordEntity | null;
  retentionDays?: number;
  activeRuns?: number;
  maxConcurrentRuns?: number;
}

export function UsageCard({
  usage,
  retentionDays = 7,
  activeRuns = 0,
  maxConcurrentRuns = 2,
}: UsageCardProps) {
  const hasUsage = usage && (usage.promptTokens > 0 || usage.completionTokens > 0);

  return (
    <div className="rounded-xl border border-border bg-card p-4 space-y-3 shadow-xs">
      <div className="flex items-center justify-between border-b border-border pb-2.5">
        <div className="flex items-center gap-2">
          <Coins className="h-4 w-4 text-amber-500" />
          <h4 className="text-xs font-semibold text-foreground uppercase tracking-wider">
            Resource Usage & Cost
          </h4>
        </div>
        <span className="text-[11px] font-mono text-muted-foreground">
          {usage?.modelName || "Model not recorded"}
        </span>
      </div>

      <div className="grid grid-cols-2 sm:grid-cols-3 gap-3 text-xs font-mono">
        <div className="p-2.5 rounded-lg bg-muted/40 border border-border/60">
          <span className="text-[10px] text-muted-foreground block font-sans">
            Prompt Tokens
          </span>
          <span className="text-sm font-semibold text-foreground">
            {hasUsage ? usage.promptTokens.toLocaleString() : "Unknown"}
          </span>
        </div>

        <div className="p-2.5 rounded-lg bg-muted/40 border border-border/60">
          <span className="text-[10px] text-muted-foreground block font-sans">
            Completion Tokens
          </span>
          <span className="text-sm font-semibold text-foreground">
            {hasUsage ? usage.completionTokens.toLocaleString() : "Unknown"}
          </span>
        </div>

        <div className="p-2.5 rounded-lg bg-muted/40 border border-border/60 col-span-2 sm:col-span-1">
          <span className="text-[10px] text-muted-foreground block font-sans">
            Estimated Cost
          </span>
          <span className="text-sm font-semibold text-foreground">
            {hasUsage && usage.estimatedCostUsd !== null && usage.estimatedCostUsd !== undefined
              ? `$${usage.estimatedCostUsd.toFixed(4)}`
              : "Unknown"}
          </span>
        </div>
      </div>

      {hasUsage ? (
        <div className="text-[11px] text-muted-foreground flex items-center gap-1.5 font-mono">
          <Info className="h-3 w-3 shrink-0" />
          <span>Provenance: {usage.priceProvenance}</span>
        </div>
      ) : (
        <div className="text-[11px] text-muted-foreground flex items-center gap-1.5">
          <Info className="h-3 w-3 shrink-0 text-amber-500" />
          <span>Usage is unknown until reported by the active OpenCode worker.</span>
        </div>
      )}

      {/* Concurrency Slot Usage */}
      <div className="p-3 rounded-lg bg-muted/40 border border-border/60 space-y-2">
        <div className="flex items-center justify-between text-xs">
          <div className="flex items-center gap-1.5 font-medium text-foreground">
            <Layers className="h-3.5 w-3.5 text-primary" />
            <span>Active Concurrency: {activeRuns} / {maxConcurrentRuns} slots in use</span>
          </div>
          <span className="text-[11px] font-mono text-muted-foreground">
            {Math.min(100, Math.round((activeRuns / (maxConcurrentRuns || 1)) * 100))}%
          </span>
        </div>
        <div className="w-full bg-muted rounded-full h-2 overflow-hidden border border-border/40">
          <div
            className={`h-full transition-all duration-300 rounded-full ${
              activeRuns >= maxConcurrentRuns ? "bg-amber-500" : "bg-primary"
            }`}
            style={{
              width: `${Math.min(100, Math.max(0, Math.round((activeRuns / (maxConcurrentRuns || 1)) * 100)))}%`,
            }}
          />
        </div>
      </div>

      {/* Retention Limits */}
      <div className="pt-2 border-t border-border/60 flex items-center justify-between text-[11px] text-muted-foreground">
        <span className="flex items-center gap-1.5">
          <HardDrive className="h-3 w-3" />
          Worktree Retention Limit:
        </span>
        <span className="font-mono font-medium text-foreground">
          {retentionDays} days until reconstruction expiry
        </span>
      </div>
    </div>
  );
}
