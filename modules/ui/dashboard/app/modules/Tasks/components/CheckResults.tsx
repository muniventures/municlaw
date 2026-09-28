import { CheckCircle2, XCircle, Terminal, Clock } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import type { CheckResultPayload } from "@/api/types";

interface CheckResultsProps {
  checks: CheckResultPayload[];
}

export function CheckResults({ checks }: CheckResultsProps) {
  const passedCount = checks.filter((c) => c.passed).length;
  const failedCount = checks.filter((c) => !c.passed).length;

  return (
    <div className="rounded-xl border border-border bg-card overflow-hidden shadow-xs">
      <div className="p-4 border-b border-border bg-muted/30 flex items-center justify-between">
        <div className="flex items-center gap-2">
          <Terminal className="h-4 w-4 text-muted-foreground" />
          <h3 className="text-sm font-semibold text-foreground">
            Test Verification & Checks
          </h3>
          <span className="text-xs font-mono text-muted-foreground">
            ({checks.length} {checks.length === 1 ? "check" : "checks"} recorded)
          </span>
        </div>

        <div className="flex items-center gap-2">
          {passedCount > 0 && (
            <Badge variant="success" className="gap-1 text-xs">
              <CheckCircle2 className="h-3 w-3" />
              {passedCount} Passed
            </Badge>
          )}
          {failedCount > 0 && (
            <Badge variant="destructive" className="gap-1 text-xs">
              <XCircle className="h-3 w-3" />
              {failedCount} Failed
            </Badge>
          )}
        </div>
      </div>

      <div className="p-4 space-y-4">
        {checks.length === 0 ? (
          <div className="p-8 text-center text-muted-foreground">
            <Clock className="h-8 w-8 mx-auto mb-2 opacity-40" />
            <p className="text-sm font-medium">No check commands executed yet</p>
            <p className="text-xs mt-1 text-muted-foreground">
              Actual test results and exit codes executed by the agent harness will appear here.
            </p>
          </div>
        ) : (
          checks.map((check, idx) => (
            <div
              key={idx}
              className={`rounded-lg border p-3.5 space-y-2.5 ${
                check.passed
                  ? "border-emerald-500/30 bg-emerald-500/5"
                  : "border-destructive/30 bg-destructive/5"
              }`}
            >
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 text-xs">
                <div className="flex items-center gap-2 font-mono">
                  {check.passed ? (
                    <CheckCircle2 className="h-4 w-4 text-emerald-600 shrink-0" />
                  ) : (
                    <XCircle className="h-4 w-4 text-destructive shrink-0" />
                  )}
                  <span className="font-semibold text-foreground break-all">
                    $ {check.command}
                  </span>
                </div>

                <div className="flex items-center gap-2 shrink-0 font-mono text-[11px]">
                  <span className="px-2 py-0.5 rounded bg-background border border-border">
                    exit code: {check.exitCode}
                  </span>
                  <span className="text-muted-foreground">
                    {check.durationMs}ms
                  </span>
                </div>
              </div>

              {/* Raw command stdout/stderr */}
              <div className="bg-black/90 text-zinc-100 rounded-md p-3 font-mono text-[11px] overflow-x-auto max-h-48 whitespace-pre leading-relaxed">
                {check.output || "(no output emitted)"}
              </div>
            </div>
          ))
        )}
      </div>
    </div>
  );
}
