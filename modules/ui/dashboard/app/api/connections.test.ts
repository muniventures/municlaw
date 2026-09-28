import { describe, it, expect, vi, beforeEach } from "vitest";
import {
  getOrganizationCredentials,
  registerOrganizationCredential,
  updateCredentialPolicy,
  getAccessibleCredentials,
} from "./connections";
import type { ProviderCredentialReference } from "./types";

describe("Organization Connections API", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  const mockOrgCred: ProviderCredentialReference = {
    id: "org-cred-1",
    organizationId: "org-1",
    owningUserId: "user-admin",
    providerName: "Anthropic",
    label: "Company Claude 3.7",
    scope: "Organization",
    isRevoked: false,
    createdAt: "2026-09-28T12:00:00Z",
    policy: {
      allowedModels: ["claude-3-7-sonnet", "claude-3-5-sonnet"],
      monthlySpendLimitUsd: 150.0,
      currentSpendUsd: 45.2,
      adminOnly: false,
    },
  };

  const mockPersonalCred: ProviderCredentialReference = {
    id: "personal-cred-1",
    organizationId: "org-1",
    owningUserId: "user-member",
    providerName: "OpenAI",
    label: "Personal GPT-4o",
    scope: "Personal",
    isRevoked: false,
    createdAt: "2026-09-28T12:00:00Z",
  };

  it("fetches organization credentials successfully", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [mockOrgCred],
    } as Response);

    const result = await getOrganizationCredentials("org-1");
    expect(result).toHaveLength(1);
    expect(result[0].id).toBe("org-cred-1");
    expect(result[0].scope).toBe("Organization");
    expect(result[0].policy?.allowedModels).toContain("claude-3-7-sonnet");
    expect(result[0].policy?.monthlySpendLimitUsd).toBe(150.0);
  });

  it("registers organization credential with scope and policy", async () => {
    let capturedBody: any;
    vi.spyOn(globalThis, "fetch").mockImplementation(async (_url, init) => {
      if (init?.body) {
        capturedBody = JSON.parse(init.body as string);
      }
      return {
        ok: true,
        status: 200,
        json: async () => ({
          ...mockOrgCred,
          label: capturedBody.label,
        }),
      } as Response;
    });

    const result = await registerOrganizationCredential("org-1", {
      providerName: "Anthropic",
      label: "New Shared Anthropic",
      apiKey: "sk-ant-secret123",
      allowedModels: ["claude-3-7-sonnet"],
      monthlySpendLimitUsd: 200,
      adminOnly: true,
      policy: {
        allowedModels: ["claude-3-7-sonnet"],
        monthlySpendLimitUsd: 200,
        currentSpendUsd: 0,
        adminOnly: true,
      },
    });

    expect(capturedBody.scope).toBe("Organization");
    expect(capturedBody.label).toBe("New Shared Anthropic");
    expect(capturedBody.adminOnly).toBe(true);
    expect(result.label).toBe("New Shared Anthropic");
  });

  it("updates credential policy", async () => {
    let capturedBody: any;
    vi.spyOn(globalThis, "fetch").mockImplementation(async (_url, init) => {
      if (init?.body) {
        capturedBody = JSON.parse(init.body as string);
      }
      return {
        ok: true,
        status: 200,
        json: async () => ({
          ...mockOrgCred,
          policy: capturedBody,
        }),
      } as Response;
    });

    const updated = await updateCredentialPolicy("org-1", "org-cred-1", {
      allowedModels: ["*"],
      monthlySpendLimitUsd: 500,
      currentSpendUsd: 45.2,
      adminOnly: true,
    });

    expect(capturedBody.allowedModels).toEqual(["*"]);
    expect(capturedBody.monthlySpendLimitUsd).toBe(500);
    expect(capturedBody.adminOnly).toBe(true);
    expect(updated.policy?.monthlySpendLimitUsd).toBe(500);
  });

  it("gets accessible credentials combining personal and organization credentials", async () => {
    vi.spyOn(globalThis, "fetch").mockImplementation(async (url) => {
      const urlStr = String(url);
      if (urlStr.includes("/credentials/accessible")) {
        return {
          ok: true,
          status: 200,
          json: async () => [mockPersonalCred, mockOrgCred],
        } as Response;
      }
      return {
        ok: false,
        status: 404,
        json: async () => ({}),
      } as Response;
    });

    const creds = await getAccessibleCredentials("org-1");
    expect(creds).toHaveLength(2);
    expect(creds.some((c) => c.scope === "Personal")).toBe(true);
    expect(creds.some((c) => c.scope === "Organization")).toBe(true);
  });

  it("falls back to merging personal and org credentials when /accessible 404s", async () => {
    vi.spyOn(globalThis, "fetch").mockImplementation(async (url) => {
      const urlStr = String(url);
      if (urlStr.includes("/credentials/accessible")) {
        return {
          ok: false,
          status: 404,
          json: async () => ({}),
        } as Response;
      }
      if (urlStr.includes("/credentials/organization")) {
        return {
          ok: true,
          status: 200,
          json: async () => [mockOrgCred],
        } as Response;
      }
      // listCredentials
      return {
        ok: true,
        status: 200,
        json: async () => [mockPersonalCred],
      } as Response;
    });

    const creds = await getAccessibleCredentials("org-1");
    expect(creds).toHaveLength(2);
    expect(creds.find((c) => c.id === "personal-cred-1")?.scope).toBe("Personal");
    expect(creds.find((c) => c.id === "org-cred-1")?.scope).toBe("Organization");
  });
});
