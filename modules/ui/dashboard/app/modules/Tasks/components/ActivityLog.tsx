import * as React from "react";
import {
  Terminal,
  Activity,
  CheckCircle2,
  XCircle,
  Clock,
  Wrench,
  ChevronDown,
  ChevronRight,
  ShieldAlert,
  ArrowDownToLine,
  RefreshCw,
  Zap,
} from "lucide-react";
import type { TaskEventDto } from "@/api/types";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";

interface ActivityLogProps {
  events: TaskEventDto[];
  sseStatus: "idle" | "connecting" | "connected" | "reconnecting" | "disconnected" | "error";
  onRefresh?: () => void;
}

export function ActivityLog({ events, sseStatus, onRefresh }: ActivityLogProps) {
  const containerRef = React.useRef<HTMLDivElement>(null);
  const [autoScroll, setAutoScroll] = React.useState(true);
  const [expandedReasoning, setExpandedReasoning] = React.useState<Record<string, boolean>>({});

  React.useEffect(() => {
    if (autoScroll && containerRef.current) {
      containerRef.current.scrollTop = containerRef.current.scrollHeight;
    }
  }, [events, autoScroll]);

  const toggleReasoning = (id: string) => {
    setExpandedReasoning((prev) => ({ ...prev, [id]: !prev[id] }));
  };

  const getSseBadge = () => {
    switch (sseStatus) {
      case "connected":
        return (
          <Badge variant="success" className="gap-1 text-[11px] font-mono">
            <span className="h-1.5 w-1.5 rounded-full bg-emerald-500 animate-pulse" />
            Live Stream
          </Badge>
        );
      case "connecting":
      case "reconnecting":
        return (
          <Badge variant="warning" className="gap-1 text-[11px] font-mono animate-pulse">
            <RefreshCw className="h-3 w-3 animate-spin" />
            Reconnecting
          </Badge>
        );
      case "error":
        return (
          <Badge variant="destructive" className="gap-1 text-[11px] font-mono">
            <XCircle className="h-3 w-3" />
            Stream Error
          </Badge>
        );
      default:
        return (
          <Badge variant="outline" className="gap-1 text-[11px] font-mono text-muted-foreground">
            Offline
          </Badge>
        );
    }
  };

  return (
    <div className="flex flex-col h-[520px] rounded-xl border border-border bg-card shadow-xs overflow-hidden">
      {/* Activity Log Header */}
      <div className="h-12 px-4 border-b border-border bg-muted/40 flex items-center justify-between shrink-0">
        <div className="flex items-center gap-2">
          <Terminal className="h-4 w-4 text-muted-foreground" />
          <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
            Live Agent Execution Log
          </span>
          <span className="text-xs font-mono text-muted-foreground">
            ({events.length} events)
          </span>
        </div>

        <div className="flex items-center gap-2">
          {getSseBadge()}
          <Button
            variant="ghost"
            size="sm"
            onClick={() => setAutoScroll((prev) => !prev)}
            className="text-xs h-7 gap-1"
          >
            <ArrowDownToLine className={`h-3.5 w-3.5 ${autoScroll ? "text-primary" : "text-muted-foreground"}`} />
            <span className="hidden sm:inline">Auto-scroll</span>
          </Button>
          {onRefresh && (
            <Button variant="ghost" size="icon" className="h-7 w-7" onClick={onRefresh} aria-label="Refresh stream">
              <RefreshCw className="h-3.5 w-3.5" />
            </Button>
          )}
        </div>
      </div>

      {/* Events Stream Body */}
      <div
        ref={containerRef}
        className="flex-1 p-4 overflow-y-auto font-mono text-xs space-y-3 bg-card"
        tabIndex={0}
        aria-label="Agent activity log"
      >
        {events.length === 0 ? (
          <div className="h-full flex flex-col items-center justify-center text-muted-foreground">
            <Activity className="h-8 w-8 mb-2 opacity-40 animate-pulse" />
            <p>Connecting to worker event stream...</p>
            <p className="text-[11px] opacity-70 mt-1">
              Events will stream automatically with monotonic replay and deduplication.
            </p>
          </div>
        ) : (
          events.map((evt) => {
            let parsedPayload: Record<string, unknown> = {};
            try {
              parsedPayload = JSON.parse(evt.payloadJson);
            } catch {
              parsedPayload = { raw: evt.payloadJson };
            }

            return (
              <div
                key={evt.eventId}
                className="group border-l-2 border-border pl-3 py-1 hover:border-primary transition-colors"
              >
                {/* Event meta */}
                <div className="flex items-center gap-2 text-[11px] text-muted-foreground mb-1">
                  <span className="font-semibold text-foreground">
                    #{evt.sequenceNumber}
                  </span>
                  <span className="px-1.5 py-0.2 rounded bg-muted text-[10px]">
                    {evt.eventType}
                  </span>
                  <span className="ml-auto opacity-70">
                    {new Date(evt.timestamp).toLocaleTimeString()}
                  </span>
                </div>

                {/* Event specific presentation */}
                {evt.eventType === "StatusChanged" && (
                  <div className="flex items-center gap-2 text-foreground font-semibold text-xs py-0.5">
                    <Zap className="h-3.5 w-3.5 text-blue-500" />
                    <span>Run transitioned to {String(parsedPayload.to || "Unknown")}</span>
                    {Boolean(parsedPayload.reason) && (
                      <span className="text-muted-foreground font-normal">
                        ({String(parsedPayload.reason)})
                      </span>
                    )}
                  </div>
                )}

                {evt.eventType === "StepStarted" && (
                  <div className="flex items-center gap-2 text-primary font-medium">
                    <Clock className="h-3.5 w-3.5" />
                    <span>Step: {String(parsedPayload.name || parsedPayload.stepId || "Step started")}</span>
                  </div>
                )}

                {evt.eventType === "StepEnded" && (
                  <div className="flex items-center gap-2 text-emerald-600 font-medium">
                    <CheckCircle2 className="h-3.5 w-3.5" />
                    <span>Step completed</span>
                    {typeof parsedPayload.durationMs === "number" && (
                      <span className="text-muted-foreground">({parsedPayload.durationMs}ms)</span>
                    )}
                  </div>
                )}

                {evt.eventType === "StepFailed" && (
                  <div className="flex items-center gap-2 text-destructive font-medium">
                    <XCircle className="h-3.5 w-3.5" />
                    <span>Step failed: {String(parsedPayload.error || "Unknown error")}</span>
                  </div>
                )}

                {evt.eventType === "ToolCalled" && (
                  <div className="p-2 rounded bg-muted/60 border border-border text-foreground space-y-1">
                    <div className="flex items-center gap-1.5 font-semibold text-blue-600 dark:text-blue-400">
                      <Wrench className="h-3.5 w-3.5" />
                      <span>Tool Call: {String(parsedPayload.toolName || "tool")}</span>
                    </div>
                    {Boolean(parsedPayload.args) && (
                      <pre className="text-[11px] overflow-x-auto p-1.5 bg-background rounded border text-muted-foreground">
                        {JSON.stringify(parsedPayload.args, null, 2)}
                      </pre>
                    )}
                  </div>
                )}

                {evt.eventType === "ToolSuccess" && (
                  <div className="p-2 rounded bg-emerald-500/10 border border-emerald-500/20 text-emerald-800 dark:text-emerald-300">
                    <span className="font-semibold">Tool Succeeded</span>
                    {Boolean(parsedPayload.result) && (
                      <pre className="mt-1 text-[11px] max-h-32 overflow-y-auto">
                        {typeof parsedPayload.result === "string"
                          ? parsedPayload.result
                          : JSON.stringify(parsedPayload.result, null, 2)}
                      </pre>
                    )}
                  </div>
                )}

                {evt.eventType === "ToolFailed" && (
                  <div className="p-2 rounded bg-destructive/10 border border-destructive/20 text-destructive">
                    <span className="font-semibold">Tool Failed</span>
                    <p className="mt-0.5 text-[11px]">{String(parsedPayload.error || "")}</p>
                  </div>
                )}

                {evt.eventType === "ReasoningDelta" && (
                  <div className="border border-border/80 rounded bg-muted/30">
                    <button
                      type="button"
                      onClick={() => toggleReasoning(evt.eventId)}
                      className="w-full flex items-center justify-between px-2.5 py-1.5 text-[11px] text-muted-foreground hover:text-foreground cursor-pointer"
                    >
                      <span className="flex items-center gap-1 font-semibold">
                        {expandedReasoning[evt.eventId] ? (
                          <ChevronDown className="h-3 w-3" />
                        ) : (
                          <ChevronRight className="h-3 w-3" />
                        )}
                        Thinking / Internal Reasoning
                      </span>
                    </button>
                    {expandedReasoning[evt.eventId] && (
                      <div className="p-2.5 pt-0 text-[11px] text-muted-foreground border-t border-border/50 whitespace-pre-wrap font-sans">
                        {String(parsedPayload.text || parsedPayload.raw || "")}
                      </div>
                    )}
                  </div>
                )}

                {evt.eventType === "TextDelta" && (
                  <div className="whitespace-pre-wrap text-foreground font-sans text-xs bg-muted/20 p-2 rounded">
                    {String(parsedPayload.text || parsedPayload.raw || "")}
                  </div>
                )}

                {evt.eventType === "ApprovalRequested" && (
                  <div className="p-2.5 rounded-lg bg-amber-500/15 border border-amber-500/30 text-amber-900 dark:text-amber-300 space-y-1">
                    <div className="flex items-center gap-1.5 font-bold">
                      <ShieldAlert className="h-4 w-4" />
                      <span>Approval Required: {String(parsedPayload.capability || "")}</span>
                    </div>
                    <p className="text-xs">{String(parsedPayload.actionDescription || "")}</p>
                  </div>
                )}

                {evt.eventType === "ApprovalResolved" && (
                  <div className="p-2 rounded bg-muted text-foreground flex items-center gap-2">
                    <CheckCircle2 className="h-3.5 w-3.5 text-primary" />
                    <span>
                      Approval resolved: {String(parsedPayload.decision || "Decided")}
                    </span>
                  </div>
                )}

                {evt.eventType === "Error" && (
                  <div className="p-2 rounded bg-destructive/15 border border-destructive/30 text-destructive font-medium">
                    {String(parsedPayload.message || parsedPayload.raw || "An error occurred")}
                  </div>
                )}
              </div>
            );
          })
        )}
      </div>
    </div>
  );
}
