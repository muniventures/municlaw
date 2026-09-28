import * as React from "react";
import {
  ShieldAlert,
  Save,
  CheckCircle2,
  Building,
  Lock,
  Info,
  Bell,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import type { CapabilityCategory, ApprovalPolicySetting, Organization } from "@/api/types";
import {
  getCapabilityPolicies,
  saveCapabilityPolicies,
  getOrganization,
} from "@/api/settings";
import { getRuntimeConfig } from "@/core/config/runtime";
import { NotificationChannels } from "./components/NotificationChannels";

interface CapabilityInfo {
  category: CapabilityCategory;
  name: string;
  description: string;
  isPlatformDenied?: boolean;
}

const CAPABILITIES: CapabilityInfo[] = [
  {
    category: "WorktreeReadWrite",
    name: "Worktree Read & Write",
    description: "Read and modify files within the isolated task worktree.",
  },
  {
    category: "LocalGit",
    name: "Local Git Operations",
    description: "Create local commits, check status, inspect local git log and branch changes.",
  },
  {
    category: "SandboxedCommand",
    name: "Sandboxed Command Execution",
    description: "Execute compilers, linters, and test runners in the isolated sandbox.",
  },
  {
    category: "PackageNetwork",
    name: "Package Registry Network",
    description: "Download package manager dependencies from npm, PyPI, or NuGet registries.",
  },
  {
    category: "ExternalNetwork",
    name: "Outbound Public Network",
    description: "Arbitrary outbound network requests outside approved package registries.",
  },
  {
    category: "DestructiveWorkspace",
    name: "Destructive Workspace Reset",
    description: "Delete files outside worktree or perform git hard resets.",
  },
  {
    category: "SetupConfig",
    name: "Environment Setup Modification",
    description: "Modify repository setup commands or container provisioning scripts.",
  },
  {
    category: "RemoteGitWrite",
    name: "Remote Git Push",
    description: "Push task branch refs directly to upstream GitHub or GitLab repositories.",
  },
  {
    category: "DraftDelivery",
    name: "Draft PR Publication",
    description: "Publish reviewed Pull Requests / Merge Requests to upstream repositories.",
  },
  {
    category: "PlatformDenied",
    name: "Platform Denied Operations",
    description: "Container host breakouts, cloud metadata access, or kernel manipulation (Strictly Denied).",
    isPlatformDenied: true,
  },
];

export function SettingsModule() {
  const [config] = React.useState(() => getRuntimeConfig());
  const [org, setOrg] = React.useState<Organization | null>(null);
  const [policies, setPolicies] = React.useState<Record<CapabilityCategory, ApprovalPolicySetting> | null>(null);
  const [activeTab, setActiveTab] = React.useState("policies");
  const [isLoading, setIsLoading] = React.useState(true);
  const [isSaving, setIsSaving] = React.useState(false);
  const [saveSuccess, setSaveSuccess] = React.useState(false);

  React.useEffect(() => {
    async function load() {
      try {
        setIsLoading(true);
        const [orgData, policyData] = await Promise.all([
          getOrganization(config.defaultOrganizationId),
          getCapabilityPolicies(config.defaultOrganizationId),
        ]);
        setOrg(orgData);
        setPolicies(policyData);
      } finally {
        setIsLoading(false);
      }
    }
    load();
  }, [config.defaultOrganizationId]);

  const handlePolicyChange = (category: CapabilityCategory, value: ApprovalPolicySetting) => {
    if (category === "PlatformDenied") return; // Invariant
    if (!policies) return;
    setPolicies({
      ...policies,
      [category]: value,
    });
    setSaveSuccess(false);
  };

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!policies) return;

    try {
      setIsSaving(true);
      await saveCapabilityPolicies(config.defaultOrganizationId, policies);
      setSaveSuccess(true);
      setTimeout(() => setSaveSuccess(false), 3000);
    } finally {
      setIsSaving(false);
    }
  };

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight text-foreground">
          Organization & Policy Settings
        </h1>
        <p className="text-sm text-muted-foreground mt-1">
          Configure security capability approval policies, outbound notifications, and organization parameters.
        </p>
      </div>

      <Tabs value={activeTab} onValueChange={setActiveTab} className="space-y-6">
        <TabsList className="grid w-full max-w-md grid-cols-3">
          <TabsTrigger value="policies" className="gap-2">
            <ShieldAlert className="h-4 w-4" />
            <span>Policies</span>
          </TabsTrigger>
          <TabsTrigger value="general" className="gap-2">
            <Building className="h-4 w-4" />
            <span>General</span>
          </TabsTrigger>
          <TabsTrigger value="notifications" className="gap-2">
            <Bell className="h-4 w-4" />
            <span>Notifications</span>
          </TabsTrigger>
        </TabsList>

        {/* Policies Tab */}
        <TabsContent value="policies">
          {isLoading || !policies ? (
            <div className="p-8 text-center text-muted-foreground font-mono text-xs rounded-xl border border-border bg-card">
              Loading security policies...
            </div>
          ) : (
            <form onSubmit={handleSave} className="space-y-6">
              {/* Capability Policy Matrix */}
              <div className="rounded-xl border border-border bg-card p-6 shadow-xs space-y-4">
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 border-b border-border pb-3">
                  <div className="flex items-center gap-2 font-semibold text-foreground text-sm">
                    <ShieldAlert className="h-4 w-4 text-primary" />
                    <span>Agent Capability Approval Policies</span>
                  </div>
                  <span className="text-xs text-muted-foreground">
                    Determines when the agent must pause and request operator sign-off.
                  </span>
                </div>

                <div className="divide-y divide-border rounded-lg border border-border overflow-hidden">
                  {CAPABILITIES.map((cap) => {
                    const currentSetting = policies[cap.category] || "Ask";
                    const isLocked = cap.isPlatformDenied;

                    return (
                      <div
                        key={cap.category}
                        className={`p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3 ${
                          isLocked ? "bg-muted/40" : "hover:bg-muted/20"
                        }`}
                      >
                        <div className="space-y-1 pr-4">
                          <div className="flex items-center gap-2">
                            <span className="font-semibold text-xs text-foreground">
                              {cap.name}
                            </span>
                            <code className="text-[10px] text-muted-foreground bg-muted px-1.5 py-0.5 rounded font-mono">
                              {cap.category}
                            </code>
                            {isLocked && (
                              <Badge variant="destructive" className="gap-1 text-[10px]">
                                <Lock className="h-2.5 w-2.5" /> Immutable Deny
                              </Badge>
                            )}
                          </div>
                          <p className="text-xs text-muted-foreground">
                            {cap.description}
                          </p>
                        </div>

                        <div className="shrink-0">
                          {isLocked ? (
                            <div className="h-9 px-3 py-1.5 rounded-md bg-destructive/15 border border-destructive/20 text-destructive text-xs font-semibold flex items-center gap-1.5">
                              <Lock className="h-3.5 w-3.5" /> Deny Always
                            </div>
                          ) : (
                            <div className="flex items-center rounded-lg border border-border bg-muted/30 p-0.5">
                              {(["Allow", "Ask", "Deny"] as ApprovalPolicySetting[]).map((option) => (
                                <button
                                  key={option}
                                  type="button"
                                  onClick={() => handlePolicyChange(cap.category, option)}
                                  className={`px-3 py-1 text-xs font-medium rounded-md transition-all cursor-pointer ${
                                    currentSetting === option
                                      ? option === "Allow"
                                        ? "bg-emerald-600 text-white shadow-xs font-semibold"
                                        : option === "Ask"
                                        ? "bg-amber-600 text-white shadow-xs font-semibold"
                                        : "bg-destructive text-destructive-foreground shadow-xs font-semibold"
                                      : "text-muted-foreground hover:text-foreground"
                                  }`}
                                >
                                  {option}
                                </button>
                              ))}
                            </div>
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>

                <div className="p-3 rounded-lg bg-muted/40 border border-border text-xs text-muted-foreground flex items-center gap-2">
                  <Info className="h-4 w-4 shrink-0 text-primary" />
                  <span>
                    <strong>Allow:</strong> Autonomous execution without interruption. <strong>Ask:</strong> Pauses run until human approval. <strong>Deny:</strong> Immediately rejected.
                  </span>
                </div>
              </div>

              {/* Action Row */}
              <div className="flex items-center justify-end gap-3 pt-2">
                {saveSuccess && (
                  <span className="text-xs font-semibold text-emerald-600 flex items-center gap-1">
                    <CheckCircle2 className="h-4 w-4" /> Policies saved successfully
                  </span>
                )}
                <Button type="submit" disabled={isSaving} className="gap-2">
                  <Save className="h-4 w-4" />
                  <span>{isSaving ? "Saving Policies..." : "Save Policies"}</span>
                </Button>
              </div>
            </form>
          )}
        </TabsContent>

        {/* General Tab */}
        <TabsContent value="general">
          {isLoading || !org ? (
            <div className="p-8 text-center text-muted-foreground font-mono text-xs rounded-xl border border-border bg-card">
              Loading organization details...
            </div>
          ) : (
            <div className="rounded-xl border border-border bg-card p-6 shadow-xs space-y-4">
              <div className="flex items-center gap-2 font-semibold text-foreground text-sm border-b border-border pb-3">
                <Building className="h-4 w-4 text-muted-foreground" />
                <span>Organization Details</span>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <div>
                  <label htmlFor="settings-org-name" className="block text-xs font-semibold text-foreground mb-1">
                    Organization Name
                  </label>
                  <Input
                    id="settings-org-name"
                    value={org?.name || ""}
                    disabled
                    className="bg-muted/40"
                  />
                </div>

                <div>
                  <label htmlFor="settings-org-slug" className="block text-xs font-semibold text-foreground mb-1">
                    Organization Slug
                  </label>
                  <Input
                    id="settings-org-slug"
                    value={org?.slug || ""}
                    disabled
                    className="bg-muted/40 font-mono text-xs"
                  />
                </div>
              </div>

              <div className="pt-2 flex items-center gap-2 text-xs text-muted-foreground">
                <Badge variant="success" className="gap-1 text-[11px]">
                  <CheckCircle2 className="h-3 w-3" /> Private Allowlisted
                </Badge>
                <span>Shared sign-in verified. Dedicated Minicloud VPS attached.</span>
              </div>
            </div>
          )}
        </TabsContent>

        {/* Notifications Tab */}
        <TabsContent value="notifications">
          <NotificationChannels organizationId={config.defaultOrganizationId} />
        </TabsContent>
      </Tabs>
    </div>
  );
}
