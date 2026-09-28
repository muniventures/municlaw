import * as React from "react";
import { KeyRound, Shield, Trash2, CheckCircle2, Lock } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";
import type { CredentialScope, ProviderCredentialReference } from "@/api/types";
import { registerCredential, revokeCredential } from "@/api/connections";

interface CredentialKeyEntryProps {
  organizationId: string;
  userId: string;
  credentials: ProviderCredentialReference[];
  onCredentialsUpdated: () => void;
}

export function CredentialKeyEntry({
  organizationId,
  userId,
  credentials,
  onCredentialsUpdated,
}: CredentialKeyEntryProps) {
  const [providerName, setProviderName] = React.useState("Anthropic");
  const [label, setLabel] = React.useState("");
  const [scope, setScope] = React.useState<CredentialScope>("Personal");
  const [apiKey, setApiKey] = React.useState("");
  const [isSubmitting, setIsSubmitting] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);
  const [success, setSuccess] = React.useState<string | null>(null);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!label.trim() || !apiKey.trim()) {
      setError("Please provide a key label and the API key value.");
      return;
    }

    try {
      setIsSubmitting(true);
      setError(null);
      setSuccess(null);

      await registerCredential(organizationId, {
        userId,
        providerName,
        label: label.trim(),
        scope,
        apiKey: apiKey.trim(),
      });

      // Clear write-only key immediately
      setApiKey("");
      setLabel("");
      setSuccess(`Provider credential '${label}' registered securely. Key is write-only.`);
      onCredentialsUpdated();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to register credential");
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleRevoke = async (credId: string) => {
    if (!confirm("Are you sure you want to revoke this credential? Active runs using this key will fail.")) {
      return;
    }

    try {
      await revokeCredential(organizationId, credId, userId);
      onCredentialsUpdated();
    } catch (err: unknown) {
      alert(err instanceof Error ? err.message : "Failed to revoke credential");
    }
  };

  return (
    <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
      {/* Registration Form */}
      <div className="lg:col-span-5 rounded-xl border border-border bg-card p-6 shadow-xs space-y-4">
        <div>
          <div className="flex items-center gap-2 font-semibold text-foreground text-sm">
            <Lock className="h-4 w-4 text-emerald-600" />
            <span>Register Provider API Key (BYOK)</span>
          </div>
          <p className="text-xs text-muted-foreground mt-1">
            Keys are written to OpenBao vault and strictly write-only. They are never returned in responses.
          </p>
        </div>

        {error && (
          <div className="p-3 rounded-lg bg-destructive/10 border border-destructive/20 text-destructive text-xs">
            {error}
          </div>
        )}

        {success && (
          <div className="p-3 rounded-lg bg-emerald-500/10 border border-emerald-500/20 text-emerald-700 dark:text-emerald-400 text-xs">
            {success}
          </div>
        )}

        <form onSubmit={handleSubmit} className="space-y-3.5">
          <div>
            <label htmlFor="cred-provider" className="block text-xs font-semibold text-foreground mb-1">
              Provider
            </label>
            <Select
              id="cred-provider"
              value={providerName}
              onChange={(e) => setProviderName(e.target.value)}
            >
              <option value="Anthropic">Anthropic (Claude 3.7 / 3.5)</option>
              <option value="OpenAI">OpenAI (GPT-4o / GPT-4.5)</option>
              <option value="OpenCode">OpenCode External Worker</option>
              <option value="Google">Google Vertex / Gemini</option>
            </Select>
          </div>

          <div>
            <label htmlFor="cred-label" className="block text-xs font-semibold text-foreground mb-1">
              Key Label
            </label>
            <Input
              id="cred-label"
              placeholder="e.g., Primary Work Claude Key"
              value={label}
              onChange={(e) => setLabel(e.target.value)}
              required
            />
          </div>

          <div>
            <label htmlFor="cred-scope" className="block text-xs font-semibold text-foreground mb-1">
              Access Scope
            </label>
            <Select
              id="cred-scope"
              value={scope}
              onChange={(e) => setScope(e.target.value as CredentialScope)}
            >
              <option value="Personal">Personal (Only visible to you)</option>
              <option value="Organization">Organization Shared (All org members)</option>
            </Select>
          </div>

          <div>
            <label htmlFor="cred-api-key" className="block text-xs font-semibold text-foreground mb-1">
              Secret API Key (Write-Only)
            </label>
            <Input
              id="cred-api-key"
              type="password"
              autoComplete="off"
              placeholder="sk-ant-... or sk-..."
              value={apiKey}
              onChange={(e) => setApiKey(e.target.value)}
              required
            />
            <p className="text-[11px] text-muted-foreground mt-1">
              Value is encrypted immediately upon saving.
            </p>
          </div>

          <Button type="submit" disabled={isSubmitting} className="w-full text-xs">
            {isSubmitting ? "Encrypting & Storing..." : "Save Write-Only Key"}
          </Button>
        </form>
      </div>

      {/* Active Credentials List */}
      <div className="lg:col-span-7 rounded-xl border border-border bg-card p-6 shadow-xs space-y-4">
        <div>
          <h3 className="text-base font-semibold text-foreground">
            Registered Provider Credentials
          </h3>
          <p className="text-xs text-muted-foreground mt-0.5">
            Manage your personal and organization BYOK credentials.
          </p>
        </div>

        <div className="divide-y divide-border rounded-lg border border-border overflow-hidden">
          {credentials.length === 0 ? (
            <div className="p-8 text-center text-muted-foreground text-xs">
              <KeyRound className="h-8 w-8 mx-auto mb-2 opacity-40" />
              <p>No provider keys registered yet.</p>
              <p className="text-[11px] mt-1">Add your Anthropic or OpenAI key on the left.</p>
            </div>
          ) : (
            credentials.map((cred) => (
              <div
                key={cred.id}
                className="p-3.5 flex items-center justify-between hover:bg-muted/20 transition-colors"
              >
                <div className="space-y-1">
                  <div className="flex items-center gap-2">
                    <span className="font-semibold text-xs text-foreground">
                      {cred.label}
                    </span>
                    <Badge variant="outline" className="text-[10px] font-mono">
                      {cred.providerName}
                    </Badge>
                    <Badge
                      variant={cred.scope === "Personal" ? "secondary" : "info"}
                      className="text-[10px]"
                    >
                      {cred.scope}
                    </Badge>
                  </div>
                  <div className="text-[11px] text-muted-foreground font-mono">
                    ID: {cred.id.slice(0, 8)}... • Added: {new Date(cred.createdAt).toLocaleDateString()}
                  </div>
                </div>

                <div>
                  {cred.isRevoked ? (
                    <Badge variant="destructive" className="text-[11px]">
                      Revoked
                    </Badge>
                  ) : (
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => handleRevoke(cred.id)}
                      className="text-xs text-destructive hover:bg-destructive/10 h-8 gap-1"
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                      <span>Revoke</span>
                    </Button>
                  )}
                </div>
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
}
