import * as React from "react";
import {
  Server,
  RefreshCw,
  Plus,
  ShieldCheck,
  Trash2,
  CheckCircle2,
  XCircle,
  Clock,
  Key,
  Globe,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Select } from "@/components/ui/select";
import type { CodingWorkspace, WorkspaceStatus } from "@/api/types";
import {
  getWorkspaceStatus,
  requestWorkspace,
  deleteWorkspace,
} from "@/api/workspace";
import { getRuntimeConfig } from "@/core/config/runtime";

export function WorkspaceModule() {
  const [config] = React.useState(() => getRuntimeConfig());
  const [workspace, setWorkspace] = React.useState<CodingWorkspace | null>(null);
  const [isLoading, setIsLoading] = React.useState(true);
  const [isRefreshing, setIsRefreshing] = React.useState(false);
  const [isProvisioning, setIsProvisioning] = React.useState(false);
  const [region, setRegion] = React.useState("us-east-1");
  const [registrationToken, setRegistrationToken] = React.useState<string | null>(null);
  const [error, setError] = React.useState<string | null>(null);

  const fetchStatus = React.useCallback(async () => {
    try {
      setError(null);
      const data = await getWorkspaceStatus(
        config.defaultOrganizationId,
        config.defaultUserId
      );
      setWorkspace(data);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to load workspace status");
    } finally {
      setIsLoading(false);
      setIsRefreshing(false);
    }
  }, [config.defaultOrganizationId, config.defaultUserId]);

  React.useEffect(() => {
    fetchStatus();
  }, [fetchStatus]);

  const handleRequestWorkspace = async () => {
    try {
      setIsProvisioning(true);
      setError(null);
      const res = await requestWorkspace(config.defaultOrganizationId, {
        userId: config.defaultUserId,
        region,
      });
      setWorkspace(res);
      if (res.registrationToken) {
        setRegistrationToken(res.registrationToken);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to request workspace");
    } finally {
      setIsProvisioning(false);
    }
  };

  const handleDelete = async () => {
    if (!confirm("Are you sure you want to terminate this VPS workspace? Running tasks will be stopped.")) {
      return;
    }

    try {
      setIsRefreshing(true);
      await deleteWorkspace(config.defaultOrganizationId, config.defaultUserId);
      setWorkspace(null);
      setRegistrationToken(null);
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Failed to terminate workspace");
    } finally {
      setIsRefreshing(false);
    }
  };

  const getStatusBadge = (status?: WorkspaceStatus) => {
    switch (status) {
      case "Ready":
        return (
          <Badge variant="success" className="gap-1.5 text-xs">
            <CheckCircle2 className="h-3.5 w-3.5" /> Ready & Active
          </Badge>
        );
      case "Pending":
        return (
          <Badge variant="warning" className="gap-1.5 text-xs animate-pulse">
            <Clock className="h-3.5 w-3.5" /> Provisioning VPS...
          </Badge>
        );
      case "Failed":
        return (
          <Badge variant="destructive" className="gap-1.5 text-xs">
            <XCircle className="h-3.5 w-3.5" /> Provisioning Failed
          </Badge>
        );
      case "Terminated":
        return (
          <Badge variant="outline" className="gap-1.5 text-xs">
            Terminated
          </Badge>
        );
      default:
        return (
          <Badge variant="outline" className="text-xs">
            Not Provisioned
          </Badge>
        );
    }
  };

  return (
    <div className="space-y-6">
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-foreground">
            Dedicated Coding Workspace
          </h1>
          <p className="text-sm text-muted-foreground mt-1">
            Minicloud-managed isolated coding VPS. Each organization has its own private workspace with sandboxed Docker & OpenCode runner.
          </p>
        </div>

        <div className="flex items-center gap-2 shrink-0">
          <Button
            variant="outline"
            size="sm"
            onClick={() => {
              setIsRefreshing(true);
              fetchStatus();
            }}
            disabled={isRefreshing}
            className="text-xs gap-1.5"
          >
            <RefreshCw className={`h-3.5 w-3.5 ${isRefreshing ? "animate-spin" : ""}`} />
            <span>Refresh Status</span>
          </Button>
        </div>
      </div>

      {error && (
        <div className="p-4 rounded-xl bg-destructive/10 border border-destructive/20 text-destructive text-sm font-medium">
          {error}
        </div>
      )}

      {isLoading ? (
        <div className="p-12 text-center text-muted-foreground font-mono text-xs">
          Checking workspace status...
        </div>
      ) : !workspace || workspace.status === "Terminated" ? (
        /* Explicit Onboarding / Provisioning Action */
        <div className="rounded-xl border border-dashed border-border bg-card p-8 md:p-12 text-center max-w-2xl mx-auto space-y-6">
          <div className="mx-auto w-12 h-12 rounded-xl bg-primary/10 flex items-center justify-center text-primary">
            <Server className="h-6 w-6" />
          </div>

          <div className="space-y-2">
            <h3 className="text-lg font-bold text-foreground">
              Request Dedicated Organization VPS
            </h3>
            <p className="text-sm text-muted-foreground">
              MuniClaw runs tasks exclusively inside an isolated Minicloud VPS provisioned for your organization.
              No shared containers or host access.
            </p>
          </div>

          <div className="max-w-xs mx-auto text-left space-y-1.5">
            <label htmlFor="workspace-region" className="block text-xs font-semibold text-foreground">
              Select Region
            </label>
            <Select
              id="workspace-region"
              value={region}
              onChange={(e) => setRegion(e.target.value)}
            >
              <option value="us-east-1">US East (N. Virginia)</option>
              <option value="eu-central-1">EU Central (Frankfurt)</option>
              <option value="ap-southeast-1">Asia Pacific (Singapore)</option>
            </Select>
          </div>

          <Button
            onClick={handleRequestWorkspace}
            disabled={isProvisioning}
            className="gap-2 px-6"
          >
            <Plus className="h-4 w-4" />
            <span>{isProvisioning ? "Provisioning VPS..." : "Provision Dedicated Workspace"}</span>
          </Button>
        </div>
      ) : (
        /* Workspace Status Card */
        <div className="rounded-xl border border-border bg-card p-6 shadow-xs space-y-6">
          <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 border-b border-border pb-4">
            <div className="flex items-center gap-3">
              <div className="p-2.5 rounded-lg bg-primary/10 text-primary">
                <Server className="h-5 w-5" />
              </div>
              <div>
                <div className="flex items-center gap-2">
                  <h3 className="text-base font-bold text-foreground">
                    Minicloud Coding VPS
                  </h3>
                  {getStatusBadge(workspace.status)}
                </div>
                <p className="text-xs text-muted-foreground mt-0.5 font-mono">
                  ID: {workspace.id}
                </p>
              </div>
            </div>

            <Button
              variant="outline"
              size="sm"
              onClick={handleDelete}
              className="text-xs text-destructive hover:bg-destructive/10 border-destructive/30 gap-1.5 self-start sm:self-center"
            >
              <Trash2 className="h-3.5 w-3.5" />
              <span>Terminate Workspace</span>
            </Button>
          </div>

          {/* Details Grid */}
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
            <div className="p-4 rounded-lg bg-muted/40 border border-border/60 space-y-1">
              <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
                <Globe className="h-3.5 w-3.5" />
                <span>VPS IP Address</span>
              </div>
              <div className="text-sm font-semibold font-mono text-foreground">
                {workspace.vpsIpAddress || "Allocating..."}
              </div>
            </div>

            <div className="p-4 rounded-lg bg-muted/40 border border-border/60 space-y-1">
              <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
                <ShieldCheck className="h-3.5 w-3.5 text-emerald-600" />
                <span>Sandboxing</span>
              </div>
              <div className="text-sm font-semibold text-foreground">
                Dedicated Minicloud VM
              </div>
            </div>

            <div className="p-4 rounded-lg bg-muted/40 border border-border/60 space-y-1">
              <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
                <Clock className="h-3.5 w-3.5" />
                <span>Provisioned At</span>
              </div>
              <div className="text-sm font-semibold font-mono text-foreground">
                {new Date(workspace.createdAt).toLocaleDateString()}
              </div>
            </div>
          </div>

          {/* Failure message if failed */}
          {workspace.failureReason && (
            <div className="p-4 rounded-lg bg-destructive/10 border border-destructive/20 text-destructive text-xs">
              <span className="font-bold block mb-1">Provisioning Failure:</span>
              {workspace.failureReason}
            </div>
          )}

          {/* Supervisor Registration Token (shown once on provision) */}
          {registrationToken && (
            <div className="p-4 rounded-lg bg-blue-500/10 border border-blue-500/20 text-foreground space-y-2">
              <div className="flex items-center gap-2 font-semibold text-xs text-blue-700 dark:text-blue-400">
                <Key className="h-4 w-4" />
                <span>One-Time Supervisor Registration Token</span>
              </div>
              <p className="text-[11px] text-muted-foreground">
                Used by the Minicloud cloud-init supervisor agent to bind this VM:
              </p>
              <pre className="p-2.5 rounded bg-background border border-border font-mono text-xs overflow-x-auto select-all">
                {registrationToken}
              </pre>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
