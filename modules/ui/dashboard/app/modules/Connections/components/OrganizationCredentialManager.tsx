import * as React from "react";
import {
  Building2,
  KeyRound,
  Shield,
  ShieldAlert,
  Sliders,
  Plus,
  Trash2,
  AlertTriangle,
  Lock,
  Cpu,
  CheckCircle2,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from "@/components/ui/dialog";
import type {
  OrganizationCredentialPolicy,
  ProviderCredentialReference,
  RegisterOrganizationCredentialRequest,
} from "@/api/types";
import {
  getOrganizationCredentials,
  registerOrganizationCredential,
  updateCredentialPolicy,
  revokeOrganizationCredential,
} from "@/api/connections";

interface OrganizationCredentialManagerProps {
  organizationId: string;
  userId: string;
  credentials?: ProviderCredentialReference[];
  onCredentialsUpdated: () => void;
}

const COMMON_MODEL_PRESETS = [
  { id: "*", label: "All Models (*)" },
  { id: "claude-3-7-sonnet", label: "Claude 3.7 Sonnet" },
  { id: "claude-3-5-sonnet", label: "Claude 3.5 Sonnet" },
  { id: "gpt-4o", label: "GPT-4o" },
  { id: "gpt-4.5-preview", label: "GPT-4.5 Preview" },
  { id: "deepseek-chat", label: "DeepSeek Chat" },
];

export function OrganizationCredentialManager({
  organizationId,
  userId,
  credentials: initialCredentials,
  onCredentialsUpdated,
}: OrganizationCredentialManagerProps) {
  const [credentials, setCredentials] = React.useState<ProviderCredentialReference[]>(
    initialCredentials || []
  );
  const [isLoading, setIsLoading] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  // Add Key Modal state
  const [isAddOpen, setIsAddOpen] = React.useState(false);
  const [providerName, setProviderName] = React.useState("Anthropic");
  const [label, setLabel] = React.useState("");
  const [apiKey, setApiKey] = React.useState("");
  const [allowedModelsText, setAllowedModelsText] = React.useState("*");
  const [hasBudgetLimit, setHasBudgetLimit] = React.useState(false);
  const [budgetLimitUsd, setBudgetLimitUsd] = React.useState("100.00");
  const [adminOnly, setAdminOnly] = React.useState(false);
  const [isSubmitting, setIsSubmitting] = React.useState(false);
  const [addError, setAddError] = React.useState<string | null>(null);

  // Edit Policy Modal state
  const [editingCred, setEditingCred] = React.useState<ProviderCredentialReference | null>(null);
  const [editAllowedModels, setEditAllowedModels] = React.useState("");
  const [editHasBudgetLimit, setEditHasBudgetLimit] = React.useState(false);
  const [editBudgetLimitUsd, setEditBudgetLimitUsd] = React.useState("");
  const [editAdminOnly, setEditAdminOnly] = React.useState(false);
  const [isUpdatingPolicy, setIsUpdatingPolicy] = React.useState(false);
  const [editError, setEditError] = React.useState<string | null>(null);

  // Revoke Confirmation Dialog state
  const [revokingCred, setRevokingCred] = React.useState<ProviderCredentialReference | null>(null);
  const [isRevoking, setIsRevoking] = React.useState(false);

  const fetchOrgCredentials = React.useCallback(async () => {
    try {
      setIsLoading(true);
      setError(null);
      const data = await getOrganizationCredentials(organizationId);
      setCredentials(data);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to load organization credentials");
    } finally {
      setIsLoading(false);
    }
  }, [organizationId]);

  React.useEffect(() => {
    if (initialCredentials && initialCredentials.length > 0) {
      setCredentials(initialCredentials);
    } else {
      fetchOrgCredentials();
    }
  }, [initialCredentials, fetchOrgCredentials]);

  const parseAllowedModels = (input: string): string[] => {
    const trimmed = input.trim();
    if (!trimmed || trimmed === "*") return ["*"];
    return trimmed
      .split(",")
      .map((s) => s.trim())
      .filter((s) => s.length > 0);
  };

  const togglePresetModel = (
    modelId: string,
    currentModelsText: string,
    setter: (val: string) => void
  ) => {
    const current = parseAllowedModels(currentModelsText);
    if (modelId === "*") {
      setter("*");
      return;
    }

    const withoutStar = current.filter((m) => m !== "*");
    let updated: string[];
    if (withoutStar.includes(modelId)) {
      updated = withoutStar.filter((m) => m !== modelId);
      if (updated.length === 0) updated = ["*"];
    } else {
      updated = [...withoutStar, modelId];
    }
    setter(updated.join(", "));
  };

  const handleOpenAddModal = () => {
    setProviderName("Anthropic");
    setLabel("");
    setApiKey("");
    setAllowedModelsText("*");
    setHasBudgetLimit(false);
    setBudgetLimitUsd("100.00");
    setAdminOnly(false);
    setAddError(null);
    setIsAddOpen(true);
  };

  const handleCreateCredential = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!label.trim() || !apiKey.trim()) {
      setAddError("Please provide both a key label and secret API key.");
      return;
    }

    try {
      setIsSubmitting(true);
      setAddError(null);

      const parsedModels = parseAllowedModels(allowedModelsText);
      const parsedLimit = hasBudgetLimit ? parseFloat(budgetLimitUsd) || null : null;

      const payload: RegisterOrganizationCredentialRequest = {
        providerName,
        label: label.trim(),
        apiKey: apiKey.trim(),
        allowedModels: parsedModels,
        monthlySpendLimitUsd: parsedLimit,
        adminOnly,
        policy: {
          allowedModels: parsedModels,
          monthlySpendLimitUsd: parsedLimit,
          currentSpendUsd: 0,
          adminOnly,
        },
      };

      await registerOrganizationCredential(organizationId, payload);
      setApiKey("");
      setIsAddOpen(false);
      await fetchOrgCredentials();
      onCredentialsUpdated();
    } catch (err: unknown) {
      setAddError(err instanceof Error ? err.message : "Failed to register organization credential");
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleOpenEditPolicy = (cred: ProviderCredentialReference) => {
    setEditingCred(cred);
    const policy = cred.policy;
    const models = policy?.allowedModels?.join(", ") || "*";
    setEditAllowedModels(models);
    setEditHasBudgetLimit(policy?.monthlySpendLimitUsd != null && policy.monthlySpendLimitUsd > 0);
    setEditBudgetLimitUsd(
      policy?.monthlySpendLimitUsd != null ? String(policy.monthlySpendLimitUsd) : "100.00"
    );
    setEditAdminOnly(Boolean(policy?.adminOnly));
    setEditError(null);
  };

  const handleUpdatePolicy = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!editingCred) return;

    try {
      setIsUpdatingPolicy(true);
      setEditError(null);

      const parsedModels = parseAllowedModels(editAllowedModels);
      const parsedLimit = editHasBudgetLimit ? parseFloat(editBudgetLimitUsd) || null : null;

      const updatedPolicy: OrganizationCredentialPolicy = {
        allowedModels: parsedModels,
        monthlySpendLimitUsd: parsedLimit,
        currentSpendUsd: editingCred.policy?.currentSpendUsd || 0,
        adminOnly: editAdminOnly,
      };

      await updateCredentialPolicy(organizationId, editingCred.id, updatedPolicy);
      setEditingCred(null);
      await fetchOrgCredentials();
      onCredentialsUpdated();
    } catch (err: unknown) {
      setEditError(err instanceof Error ? err.message : "Failed to update policy");
    } finally {
      setIsUpdatingPolicy(false);
    }
  };

  const handleConfirmRevoke = async () => {
    if (!revokingCred) return;
    try {
      setIsRevoking(true);
      await revokeOrganizationCredential(organizationId, revokingCred.id, userId);
      setRevokingCred(null);
      await fetchOrgCredentials();
      onCredentialsUpdated();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Failed to revoke credential");
    } finally {
      setIsRevoking(false);
    }
  };

  const renderSpendTracker = (policy?: OrganizationCredentialPolicy) => {
    const current = policy?.currentSpendUsd ?? 0;
    const limit = policy?.monthlySpendLimitUsd;

    if (limit == null || limit <= 0) {
      return (
        <div className="space-y-1">
          <div className="flex items-center justify-between text-xs">
            <span className="text-muted-foreground font-mono">Monthly Spend:</span>
            <span className="font-semibold text-foreground font-mono">
              ${current.toFixed(2)} / Unlimited
            </span>
          </div>
          <div className="w-full h-1.5 rounded-full bg-muted overflow-hidden">
            <div className="h-full bg-emerald-500 rounded-full" style={{ width: "8%" }} />
          </div>
        </div>
      );
    }

    const pct = Math.min(100, Math.max(0, Math.round((current / limit) * 100)));
    const isExceeded = current >= limit;
    const isNearLimit = pct >= 80;

    let barColor = "bg-emerald-500";
    if (isExceeded) {
      barColor = "bg-destructive";
    } else if (isNearLimit) {
      barColor = "bg-amber-500";
    }

    return (
      <div className="space-y-1">
        <div className="flex items-center justify-between text-xs">
          <span className="text-muted-foreground font-mono">Monthly Budget:</span>
          <span className="font-semibold text-foreground font-mono">
            ${current.toFixed(2)} / ${limit.toFixed(2)} ({pct}%)
          </span>
        </div>
        <div className="w-full h-2 rounded-full bg-muted overflow-hidden">
          <div
            className={`h-full ${barColor} rounded-full transition-all duration-300`}
            style={{ width: `${pct}%` }}
          />
        </div>
        {isExceeded && (
          <p className="text-[10px] text-destructive font-medium flex items-center gap-1">
            <AlertTriangle className="h-3 w-3" /> Budget cap exceeded; task dispatch blocked.
          </p>
        )}
      </div>
    );
  };

  return (
    <div className="space-y-6">
      {/* Header and Add Action */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <div className="flex items-center gap-2">
            <Building2 className="h-5 w-5 text-indigo-600 dark:text-indigo-400" />
            <h2 className="text-lg font-semibold tracking-tight text-foreground">
              Organization-Shared Provider Keys
            </h2>
          </div>
          <p className="text-xs text-muted-foreground mt-0.5">
            Centrally managed AI credentials with model allowlists, spend caps, and role controls.
          </p>
        </div>

        <Button
          onClick={handleOpenAddModal}
          size="sm"
          className="gap-1.5 text-xs bg-indigo-600 hover:bg-indigo-700 text-white shrink-0"
        >
          <Plus className="h-3.5 w-3.5" />
          <span>Add Organization Key</span>
        </Button>
      </div>

      {error && (
        <div className="p-3 rounded-lg bg-destructive/10 border border-destructive/20 text-destructive text-xs">
          {error}
        </div>
      )}

      {/* Shared Credentials List */}
      <div className="rounded-xl border border-border bg-card shadow-xs overflow-hidden">
        {isLoading && credentials.length === 0 ? (
          <div className="p-8 text-center text-muted-foreground text-xs font-mono">
            Loading organization credentials...
          </div>
        ) : credentials.length === 0 ? (
          <div className="p-10 text-center text-muted-foreground space-y-3">
            <KeyRound className="h-10 w-10 mx-auto opacity-30 text-indigo-500" />
            <div className="space-y-1">
              <p className="text-sm font-semibold text-foreground">No shared organization keys registered</p>
              <p className="text-xs text-muted-foreground max-w-md mx-auto">
                Add a shared provider key to allow team members to run autonomous tasks without needing
                individual personal keys.
              </p>
            </div>
            <Button
              onClick={handleOpenAddModal}
              variant="outline"
              size="sm"
              className="gap-1.5 text-xs mt-2"
            >
              <Plus className="h-3.5 w-3.5" />
              Register Shared Key
            </Button>
          </div>
        ) : (
          <div className="divide-y divide-border">
            {credentials.map((cred) => {
              const policy = cred.policy;
              const allowedModels = policy?.allowedModels || ["*"];
              const isAllModels = allowedModels.includes("*");

              return (
                <div
                  key={cred.id}
                  className="p-5 flex flex-col md:flex-row md:items-center justify-between gap-4 hover:bg-muted/15 transition-colors"
                >
                  <div className="space-y-2.5 flex-1 min-w-0">
                    {/* Header line: Provider Badge, Label, Admin-Only Tag */}
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-semibold text-sm text-foreground truncate">
                        {cred.label}
                      </span>

                      {/* Provider Badge */}
                      <Badge variant="outline" className="text-[11px] font-mono gap-1 border-indigo-500/30">
                        <Cpu className="h-3 w-3 text-indigo-500" />
                        {cred.providerName}
                      </Badge>

                      {/* Scope Badge */}
                      <Badge variant="info" className="text-[11px]">
                        Org Shared
                      </Badge>

                      {/* Admin-Only or Member Access Tag */}
                      {policy?.adminOnly ? (
                        <Badge variant="warning" className="text-[11px] gap-1">
                          <ShieldAlert className="h-3 w-3" />
                          Admin Only
                        </Badge>
                      ) : (
                        <Badge variant="secondary" className="text-[11px] gap-1">
                          <Shield className="h-3 w-3 text-emerald-600" />
                          All Members
                        </Badge>
                      )}

                      {cred.isRevoked && (
                        <Badge variant="destructive" className="text-[11px]">
                          Revoked
                        </Badge>
                      )}
                    </div>

                    {/* Policy Details: Allowed Models */}
                    <div className="flex flex-wrap items-center gap-1.5 text-xs text-muted-foreground">
                      <span className="font-medium text-foreground text-[11px]">Allowed Models:</span>
                      {isAllModels ? (
                        <span className="px-2 py-0.5 rounded text-[11px] font-mono bg-muted text-foreground border border-border">
                          All Models (*)
                        </span>
                      ) : (
                        allowedModels.map((m) => (
                          <span
                            key={m}
                            className="px-2 py-0.5 rounded text-[11px] font-mono bg-muted/80 text-foreground border border-border"
                          >
                            {m}
                          </span>
                        ))
                      )}
                    </div>

                    {/* Spend Tracker Progress Bar */}
                    <div className="max-w-md pt-1">
                      {renderSpendTracker(policy)}
                    </div>

                    {/* Metadata line */}
                    <div className="text-[11px] text-muted-foreground font-mono flex items-center gap-3">
                      <span>ID: {cred.id.slice(0, 8)}...</span>
                      <span>•</span>
                      <span>Added: {new Date(cred.createdAt).toLocaleDateString()}</span>
                    </div>
                  </div>

                  {/* Action Buttons */}
                  <div className="flex items-center gap-2 shrink-0 self-end md:self-center">
                    {!cred.isRevoked ? (
                      <>
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => handleOpenEditPolicy(cred)}
                          className="h-8 text-xs gap-1.5"
                        >
                          <Sliders className="h-3.5 w-3.5" />
                          <span>Edit Policy</span>
                        </Button>

                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setRevokingCred(cred)}
                          className="h-8 text-xs text-destructive hover:bg-destructive/10 gap-1.5"
                        >
                          <Trash2 className="h-3.5 w-3.5" />
                          <span>Revoke</span>
                        </Button>
                      </>
                    ) : (
                      <span className="text-xs text-muted-foreground italic font-mono">
                        Revoked on {cred.revokedAt ? new Date(cred.revokedAt).toLocaleDateString() : "earlier"}
                      </span>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Add Organization Key Modal */}
      <Dialog open={isAddOpen} onOpenChange={setIsAddOpen}>
        <DialogContent onClose={() => setIsAddOpen(false)} className="max-w-lg">
          <form onSubmit={handleCreateCredential}>
            <DialogHeader>
              <div className="flex items-center gap-2">
                <div className="p-1.5 rounded-lg bg-indigo-500/10 text-indigo-600">
                  <Lock className="h-5 w-5" />
                </div>
                <div>
                  <DialogTitle>Add Organization API Key</DialogTitle>
                  <DialogDescription>
                    Store an encrypted, write-only shared provider credential with access policies.
                  </DialogDescription>
                </div>
              </div>
            </DialogHeader>

            {addError && (
              <div className="p-3 mb-4 rounded-lg bg-destructive/10 border border-destructive/20 text-destructive text-xs">
                {addError}
              </div>
            )}

            <div className="space-y-4 py-2">
              {/* Provider Selector */}
              <div>
                <label htmlFor="org-cred-provider" className="block text-xs font-semibold text-foreground mb-1">
                  AI Model Provider
                </label>
                <Select
                  id="org-cred-provider"
                  value={providerName}
                  onChange={(e) => setProviderName(e.target.value)}
                >
                  <option value="Anthropic">Anthropic (Claude 3.7 / 3.5)</option>
                  <option value="OpenAI">OpenAI (GPT-4o / GPT-4.5)</option>
                  <option value="DeepSeek">DeepSeek (V3 / R1)</option>
                  <option value="Google">Google Vertex / Gemini</option>
                  <option value="OpenCode">OpenCode External Worker</option>
                </Select>
              </div>

              {/* Key Label */}
              <div>
                <label htmlFor="org-cred-label" className="block text-xs font-semibold text-foreground mb-1">
                  Key Label / Identifier
                </label>
                <Input
                  id="org-cred-label"
                  placeholder="e.g., Company Anthropic - Tier 4"
                  value={label}
                  onChange={(e) => setLabel(e.target.value)}
                  required
                />
                <p className="text-[11px] text-muted-foreground mt-0.5">
                  A clear name shown to organization members when selecting credentials.
                </p>
              </div>

              {/* Secret Key (Write-Only) */}
              <div>
                <label htmlFor="org-cred-key" className="block text-xs font-semibold text-foreground mb-1">
                  Secret API Key (Write-Only)
                </label>
                <Input
                  id="org-cred-key"
                  type="password"
                  autoComplete="off"
                  placeholder="sk-ant-... or sk-..."
                  value={apiKey}
                  onChange={(e) => setApiKey(e.target.value)}
                  required
                />
                <p className="text-[11px] text-muted-foreground mt-0.5">
                  Stored directly in OpenBao secret vault. MuniClaw never exposes or returns raw keys.
                </p>
              </div>

              {/* Allowed Models Policy */}
              <div className="space-y-2 pt-1 border-t border-border">
                <label className="block text-xs font-semibold text-foreground">
                  Allowed Models Policy
                </label>
                <p className="text-[11px] text-muted-foreground">
                  Quickly choose allowed models or specify comma-separated identifiers (* allows all).
                </p>
                <div className="flex flex-wrap gap-1.5">
                  {COMMON_MODEL_PRESETS.map((preset) => {
                    const currentModels = parseAllowedModels(allowedModelsText);
                    const isSelected =
                      preset.id === "*"
                        ? currentModels.includes("*")
                        : currentModels.includes(preset.id);

                    return (
                      <button
                        key={preset.id}
                        type="button"
                        onClick={() =>
                          togglePresetModel(preset.id, allowedModelsText, setAllowedModelsText)
                        }
                        className={`px-2.5 py-1 rounded-md text-xs font-mono transition-colors border ${
                          isSelected
                            ? "bg-indigo-600 text-white border-indigo-600"
                            : "bg-muted text-muted-foreground border-border hover:text-foreground"
                        }`}
                      >
                        {preset.label}
                      </button>
                    );
                  })}
                </div>
                <Input
                  id="org-cred-allowed-models"
                  placeholder="e.g., claude-3-7-sonnet, claude-3-5-sonnet or *"
                  value={allowedModelsText}
                  onChange={(e) => setAllowedModelsText(e.target.value)}
                />
              </div>

              {/* Monthly Spend Budget */}
              <div className="space-y-2 pt-1 border-t border-border">
                <div className="flex items-center justify-between">
                  <label htmlFor="org-cred-budget-check" className="text-xs font-semibold text-foreground flex items-center gap-2 cursor-pointer">
                    <input
                      id="org-cred-budget-check"
                      type="checkbox"
                      checked={hasBudgetLimit}
                      onChange={(e) => setHasBudgetLimit(e.target.checked)}
                      className="rounded border-input text-indigo-600 focus:ring-indigo-500 h-4 w-4"
                    />
                    <span>Enforce Monthly Spend Budget ($ USD)</span>
                  </label>
                </div>
                {hasBudgetLimit && (
                  <div className="pl-6">
                    <Input
                      id="org-cred-budget-val"
                      type="number"
                      step="5.00"
                      min="1.00"
                      max="10000.00"
                      placeholder="100.00"
                      value={budgetLimitUsd}
                      onChange={(e) => setBudgetLimitUsd(e.target.value)}
                    />
                    <p className="text-[11px] text-muted-foreground mt-1">
                      Tasks attempting to run with this key when the limit is exceeded will be rejected.
                    </p>
                  </div>
                )}
              </div>

              {/* Admin-Only Toggle */}
              <div className="pt-1 border-t border-border">
                <label htmlFor="org-cred-admin-only" className="text-xs font-semibold text-foreground flex items-center gap-2 cursor-pointer">
                  <input
                    id="org-cred-admin-only"
                    type="checkbox"
                    checked={adminOnly}
                    onChange={(e) => setAdminOnly(e.target.checked)}
                    className="rounded border-input text-indigo-600 focus:ring-indigo-500 h-4 w-4"
                  />
                  <span>Restrict to Organization Administrators Only</span>
                </label>
                <p className="text-[11px] text-muted-foreground pl-6 mt-0.5">
                  If enabled, regular organization members cannot select or dispatch tasks with this key.
                </p>
              </div>
            </div>

            <DialogFooter>
              <Button
                type="button"
                variant="outline"
                onClick={() => setIsAddOpen(false)}
                disabled={isSubmitting}
              >
                Cancel
              </Button>
              <Button
                type="submit"
                disabled={isSubmitting}
                className="bg-indigo-600 hover:bg-indigo-700 text-white"
              >
                {isSubmitting ? "Encrypting & Storing..." : "Save Shared Key"}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>

      {/* Edit Policy Modal */}
      <Dialog open={editingCred !== null} onOpenChange={(open) => !open && setEditingCred(null)}>
        <DialogContent onClose={() => setEditingCred(null)} className="max-w-lg">
          <form onSubmit={handleUpdatePolicy}>
            <DialogHeader>
              <div className="flex items-center gap-2">
                <div className="p-1.5 rounded-lg bg-indigo-500/10 text-indigo-600">
                  <Sliders className="h-5 w-5" />
                </div>
                <div>
                  <DialogTitle>Edit Access Policy</DialogTitle>
                  <DialogDescription>
                    Update model allowlist, monthly budget cap, and admin restriction for &apos;{editingCred?.label}&apos;.
                  </DialogDescription>
                </div>
              </div>
            </DialogHeader>

            {editError && (
              <div className="p-3 mb-4 rounded-lg bg-destructive/10 border border-destructive/20 text-destructive text-xs">
                {editError}
              </div>
            )}

            <div className="space-y-4 py-2">
              {/* Spend overview */}
              <div className="p-3 rounded-lg bg-muted/60 border border-border text-xs flex items-center justify-between">
                <span className="text-muted-foreground">Current Recorded Spend:</span>
                <span className="font-semibold font-mono text-foreground">
                  ${(editingCred?.policy?.currentSpendUsd || 0).toFixed(2)} USD
                </span>
              </div>

              {/* Allowed Models */}
              <div className="space-y-2">
                <label className="block text-xs font-semibold text-foreground">
                  Allowed Models
                </label>
                <div className="flex flex-wrap gap-1.5">
                  {COMMON_MODEL_PRESETS.map((preset) => {
                    const currentModels = parseAllowedModels(editAllowedModels);
                    const isSelected =
                      preset.id === "*"
                        ? currentModels.includes("*")
                        : currentModels.includes(preset.id);

                    return (
                      <button
                        key={preset.id}
                        type="button"
                        onClick={() =>
                          togglePresetModel(preset.id, editAllowedModels, setEditAllowedModels)
                        }
                        className={`px-2.5 py-1 rounded-md text-xs font-mono transition-colors border ${
                          isSelected
                            ? "bg-indigo-600 text-white border-indigo-600"
                            : "bg-muted text-muted-foreground border-border hover:text-foreground"
                        }`}
                      >
                        {preset.label}
                      </button>
                    );
                  })}
                </div>
                <Input
                  id="edit-allowed-models"
                  placeholder="e.g., claude-3-7-sonnet, claude-3-5-sonnet or *"
                  value={editAllowedModels}
                  onChange={(e) => setEditAllowedModels(e.target.value)}
                />
              </div>

              {/* Monthly Budget Cap */}
              <div className="space-y-2 pt-1 border-t border-border">
                <label htmlFor="edit-budget-check" className="text-xs font-semibold text-foreground flex items-center gap-2 cursor-pointer">
                  <input
                    id="edit-budget-check"
                    type="checkbox"
                    checked={editHasBudgetLimit}
                    onChange={(e) => setEditHasBudgetLimit(e.target.checked)}
                    className="rounded border-input text-indigo-600 focus:ring-indigo-500 h-4 w-4"
                  />
                  <span>Enforce Monthly Spend Budget ($ USD)</span>
                </label>
                {editHasBudgetLimit && (
                  <div className="pl-6">
                    <Input
                      id="edit-budget-val"
                      type="number"
                      step="5.00"
                      min="1.00"
                      max="10000.00"
                      value={editBudgetLimitUsd}
                      onChange={(e) => setEditBudgetLimitUsd(e.target.value)}
                    />
                  </div>
                )}
              </div>

              {/* Admin Only Toggle */}
              <div className="pt-1 border-t border-border">
                <label htmlFor="edit-admin-only" className="text-xs font-semibold text-foreground flex items-center gap-2 cursor-pointer">
                  <input
                    id="edit-admin-only"
                    type="checkbox"
                    checked={editAdminOnly}
                    onChange={(e) => setEditAdminOnly(e.target.checked)}
                    className="rounded border-input text-indigo-600 focus:ring-indigo-500 h-4 w-4"
                  />
                  <span>Restrict to Organization Administrators Only</span>
                </label>
                <p className="text-[11px] text-muted-foreground pl-6 mt-0.5">
                  When enabled, standard members will not be permitted to use this key.
                </p>
              </div>
            </div>

            <DialogFooter>
              <Button
                type="button"
                variant="outline"
                onClick={() => setEditingCred(null)}
                disabled={isUpdatingPolicy}
              >
                Cancel
              </Button>
              <Button
                type="submit"
                disabled={isUpdatingPolicy}
                className="bg-indigo-600 hover:bg-indigo-700 text-white"
              >
                {isUpdatingPolicy ? "Saving Policy..." : "Save Policy"}
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>

      {/* Revocation Confirmation Dialog */}
      <Dialog open={revokingCred !== null} onOpenChange={(open) => !open && setRevokingCred(null)}>
        <DialogContent onClose={() => setRevokingCred(null)} className="max-w-md">
          <DialogHeader>
            <div className="flex items-center gap-2">
              <div className="p-2 rounded-full bg-destructive/10 text-destructive">
                <AlertTriangle className="h-5 w-5" />
              </div>
              <div>
                <DialogTitle>Revoke Shared Credential?</DialogTitle>
                <DialogDescription>
                  This action is permanent and cannot be undone.
                </DialogDescription>
              </div>
            </div>
          </DialogHeader>

          <div className="py-3 text-sm text-foreground space-y-2">
            <p>
              Are you sure you want to revoke{" "}
              <span className="font-semibold">&apos;{revokingCred?.label}&apos;</span>?
            </p>
            <p className="text-xs text-muted-foreground">
              Any autonomous coding tasks actively running that rely on this credential will be
              aborted and fail immediately.
            </p>
          </div>

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => setRevokingCred(null)}
              disabled={isRevoking}
            >
              Cancel
            </Button>
            <Button
              type="button"
              variant="destructive"
              onClick={handleConfirmRevoke}
              disabled={isRevoking}
            >
              {isRevoking ? "Revoking..." : "Confirm Revocation"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
