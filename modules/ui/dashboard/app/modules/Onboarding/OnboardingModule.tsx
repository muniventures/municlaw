import * as React from "react";
import { Link } from "react-router";
import { ShieldCheck, ArrowRight, Building, CheckCircle2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { getRuntimeConfig } from "@/core/config/runtime";

export function OnboardingModule() {
  const [config] = React.useState(() => getRuntimeConfig());

  return (
    <div className="max-w-3xl mx-auto py-8 space-y-8">
      <div className="text-center space-y-3">
        <div className="inline-flex p-3 rounded-2xl bg-primary/10 text-primary mb-1">
          <ShieldCheck className="h-8 w-8" />
        </div>
        <h1 className="text-3xl font-extrabold tracking-tight text-foreground">
          Welcome to MuniClaw Console
        </h1>
        <p className="text-muted-foreground text-sm max-w-xl mx-auto">
          Private hosted autonomous coding platform. Each organization receives dedicated VPS isolation on Minicloud with BYOK credentials.
        </p>
      </div>

      <div className="rounded-xl border border-border bg-card p-6 shadow-sm space-y-6">
        <div className="flex items-center justify-between border-b border-border pb-4">
          <div className="flex items-center gap-3">
            <Building className="h-5 w-5 text-primary" />
            <div>
              <h3 className="font-semibold text-sm text-foreground">
                Organization Allowlist Verification
              </h3>
              <p className="text-xs text-muted-foreground">
                Shared sign-in does not bypass MuniClaw private preview allowlisting.
              </p>
            </div>
          </div>
          <Badge variant="success" className="gap-1 text-xs">
            <CheckCircle2 className="h-3.5 w-3.5" /> Allowlisted
          </Badge>
        </div>

        <div className="space-y-4 text-xs text-muted-foreground leading-relaxed">
          <div className="p-4 rounded-lg bg-muted/40 border border-border space-y-2">
            <div className="font-semibold text-foreground text-sm">
              Your Setup Checklist:
            </div>
            <ul className="space-y-2 list-disc list-inside">
              <li>
                <strong className="text-foreground">Dedicated VPS:</strong> Request your isolated Minicloud VPS in the Workspace tab.
              </li>
              <li>
                <strong className="text-foreground">BYOK API Key:</strong> Store your Anthropic or OpenAI API key in Connections (write-only).
              </li>
              <li>
                <strong className="text-foreground">Approval Policies:</strong> Review capabilities under Settings before running tasks.
              </li>
            </ul>
          </div>
        </div>

        <div className="flex justify-end pt-2">
          <Link to="/tasks">
            <Button className="gap-2">
              <span>Go to Tasks Console</span>
              <ArrowRight className="h-4 w-4" />
            </Button>
          </Link>
        </div>
      </div>
    </div>
  );
}
