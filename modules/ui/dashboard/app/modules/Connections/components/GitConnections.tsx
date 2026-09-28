import { GitBranch, Github, Gitlab, CheckCircle2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import type { RepositoryConnection } from "@/api/types";

interface GitConnectionsProps {
  connections: RepositoryConnection[];
}

export function GitConnections({ connections }: GitConnectionsProps) {
  return (
    <div className="rounded-xl border border-border bg-card p-6 shadow-xs space-y-4">
      <div className="flex items-center justify-between">
        <div>
          <h3 className="text-base font-semibold text-foreground">
            Git Host Connections
          </h3>
          <p className="text-xs text-muted-foreground mt-0.5">
            MuniClaw connects to your repositories via OAuth App or GitLab Project tokens.
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" className="gap-1.5 text-xs">
            <Github className="h-3.5 w-3.5" /> Connect GitHub
          </Button>
          <Button variant="outline" size="sm" className="gap-1.5 text-xs">
            <Gitlab className="h-3.5 w-3.5 text-orange-500" /> Connect GitLab
          </Button>
        </div>
      </div>

      <div className="divide-y divide-border rounded-lg border border-border overflow-hidden">
        {connections.length === 0 ? (
          <div className="p-6 text-center text-muted-foreground text-xs">
            No repository connections linked yet.
          </div>
        ) : (
          connections.map((conn) => (
            <div
              key={conn.id}
              className="p-3.5 flex items-center justify-between hover:bg-muted/30 transition-colors"
            >
              <div className="flex items-center gap-3">
                {conn.gitHost === "GitHub" ? (
                  <Github className="h-5 w-5 text-foreground" />
                ) : (
                  <Gitlab className="h-5 w-5 text-orange-500" />
                )}
                <div>
                  <div className="font-semibold text-xs text-foreground font-mono">
                    {conn.repositoryFullName}
                  </div>
                  <div className="text-[11px] text-muted-foreground flex items-center gap-2 mt-0.5">
                    <span className="flex items-center gap-1 font-mono">
                      <GitBranch className="h-3 w-3" />
                      {conn.defaultBranch}
                    </span>
                    <span>•</span>
                    <span>Account: {conn.externalAccountId}</span>
                  </div>
                </div>
              </div>

              <div className="flex items-center gap-3">
                <Badge variant="success" className="gap-1 text-[11px]">
                  <CheckCircle2 className="h-3 w-3" /> Active
                </Badge>
              </div>
            </div>
          ))
        )}
      </div>
    </div>
  );
}
