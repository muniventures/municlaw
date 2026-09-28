import * as React from "react";
import { FolderGit2, GitBranch, Terminal, CheckCircle2 } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import type { Project } from "@/api/types";
import { listProjects } from "@/api/connections";
import { getRuntimeConfig } from "@/core/config/runtime";

export function ProjectsModule() {
  const [config] = React.useState(() => getRuntimeConfig());
  const [projects, setProjects] = React.useState<Project[]>([]);
  const [isLoading, setIsLoading] = React.useState(true);

  React.useEffect(() => {
    async function load() {
      try {
        setIsLoading(true);
        const data = await listProjects(config.defaultOrganizationId);
        setProjects(data);
      } finally {
        setIsLoading(false);
      }
    }
    load();
  }, [config.defaultOrganizationId]);

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight text-foreground">
          Repository Projects
        </h1>
        <p className="text-sm text-muted-foreground mt-1">
          Configured repository targets for autonomous agent branches, base branches, and setup scripts.
        </p>
      </div>

      {isLoading ? (
        <div className="p-8 text-center text-muted-foreground font-mono text-xs">
          Loading projects...
        </div>
      ) : projects.length === 0 ? (
        <div className="p-12 text-center rounded-xl border border-dashed border-border bg-card">
          <FolderGit2 className="h-10 w-10 text-muted-foreground mx-auto mb-3 opacity-60" />
          <h3 className="text-base font-semibold text-foreground mb-1">
            No projects linked
          </h3>
          <p className="text-sm text-muted-foreground max-w-sm mx-auto">
            Connect a GitHub or GitLab repository in Connections to create your first project.
          </p>
        </div>
      ) : (
        <div className="grid gap-4">
          {projects.map((proj) => (
            <div
              key={proj.id}
              className="rounded-xl border border-border bg-card p-6 shadow-xs space-y-4"
            >
              <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 border-b border-border pb-3">
                <div className="flex items-center gap-2.5">
                  <div className="p-2 rounded-lg bg-primary/10 text-primary">
                    <FolderGit2 className="h-5 w-5" />
                  </div>
                  <div>
                    <h3 className="font-semibold text-sm text-foreground">
                      {proj.name}
                    </h3>
                    <div className="text-[11px] text-muted-foreground font-mono mt-0.5">
                      Target Repo: {proj.repositoryConnection?.repositoryFullName || proj.name}
                    </div>
                  </div>
                </div>

                <div className="flex items-center gap-2">
                  <Badge variant="outline" className="gap-1 font-mono text-xs">
                    <GitBranch className="h-3 w-3" />
                    Default: {proj.defaultBaseBranch}
                  </Badge>
                  {proj.setupCommandsConfirmed && (
                    <Badge variant="success" className="gap-1 text-xs">
                      <CheckCircle2 className="h-3 w-3" /> Setup Confirmed
                    </Badge>
                  )}
                </div>
              </div>

              {/* Setup Commands */}
              <div className="p-3.5 rounded-lg bg-muted/30 border border-border space-y-1.5 font-mono text-xs">
                <div className="flex items-center gap-1.5 text-muted-foreground font-sans font-medium text-[11px]">
                  <Terminal className="h-3.5 w-3.5 text-primary" />
                  <span>Workspace Warm-up / Setup Commands:</span>
                </div>
                <pre className="p-2 rounded bg-background border border-border text-foreground overflow-x-auto text-[11px]">
                  {proj.setupCommands || "(default: standard package installation)"}
                </pre>
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
