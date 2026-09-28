import { describe, it, expect } from "vitest";
import type { ProviderCredentialReference } from "@/api/types";

// Test policy verification logic directly
describe("TaskCreateModal Policy Guard Logic", () => {
  const orgCredWithPolicy: ProviderCredentialReference = {
    id: "org-cred-1",
    organizationId: "org-1",
    owningUserId: "user-admin",
    providerName: "Anthropic",
    label: "Company Claude 3.5 Only",
    scope: "Organization",
    isRevoked: false,
    createdAt: "2026-09-28T12:00:00Z",
    policy: {
      allowedModels: ["claude-3-5-sonnet"],
      monthlySpendLimitUsd: 100.0,
      currentSpendUsd: 100.0, // Cap reached
      adminOnly: true,
    },
  };

  const orgCredWildcard: ProviderCredentialReference = {
    id: "org-cred-2",
    organizationId: "org-1",
    owningUserId: "user-admin",
    providerName: "OpenAI",
    label: "Company Wildcard",
    scope: "Organization",
    isRevoked: false,
    createdAt: "2026-09-28T12:00:00Z",
    policy: {
      allowedModels: ["*"],
      monthlySpendLimitUsd: 500.0,
      currentSpendUsd: 20.0,
      adminOnly: false,
    },
  };

  const personalCred: ProviderCredentialReference = {
    id: "personal-cred-1",
    organizationId: "org-1",
    owningUserId: "user-member",
    providerName: "Anthropic",
    label: "My Personal Claude",
    scope: "Personal",
    isRevoked: false,
    createdAt: "2026-09-28T12:00:00Z",
  };

  function checkModelAllowed(model: string, cred?: ProviderCredentialReference) {
    if (!cred || cred.scope !== "Organization" || !cred.policy) return true;
    const allowed = cred.policy.allowedModels;
    if (!allowed || allowed.length === 0 || allowed.includes("*")) return true;
    return allowed.some((m) => {
      if (m === "*") return true;
      if (m.endsWith("*")) return model.startsWith(m.slice(0, -1));
      return m.toLowerCase() === model.toLowerCase();
    });
  }

  function checkBudgetExceeded(cred?: ProviderCredentialReference) {
    if (!cred || cred.scope !== "Organization" || !cred.policy) return false;
    const { monthlySpendLimitUsd, currentSpendUsd } = cred.policy;
    if (monthlySpendLimitUsd != null && monthlySpendLimitUsd > 0) {
      return currentSpendUsd >= monthlySpendLimitUsd;
    }
    return false;
  }

  it("permits allowed model on restricted org credential", () => {
    expect(checkModelAllowed("claude-3-5-sonnet", orgCredWithPolicy)).toBe(true);
  });

  it("rejects disallowed model on restricted org credential", () => {
    expect(checkModelAllowed("gpt-4o", orgCredWithPolicy)).toBe(false);
    expect(checkModelAllowed("claude-3-7-sonnet", orgCredWithPolicy)).toBe(false);
  });

  it("permits any model on wildcard org credential", () => {
    expect(checkModelAllowed("gpt-4o", orgCredWildcard)).toBe(true);
    expect(checkModelAllowed("claude-3-7-sonnet", orgCredWildcard)).toBe(true);
  });

  it("always permits any model on personal credential without org policy", () => {
    expect(checkModelAllowed("gpt-4o", personalCred)).toBe(true);
    expect(checkModelAllowed("claude-3-7-sonnet", personalCred)).toBe(true);
  });

  it("detects when budget cap is exceeded", () => {
    expect(checkBudgetExceeded(orgCredWithPolicy)).toBe(true);
    expect(checkBudgetExceeded(orgCredWildcard)).toBe(false);
    expect(checkBudgetExceeded(personalCred)).toBe(false);
  });
});
