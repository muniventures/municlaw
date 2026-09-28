import { apiClient } from "./client";
import type {
  ApprovalDecisionDto,
  ApprovalDecisionType,
  ApprovalPolicySetting,
  ApprovalRequestDto,
  CapabilityCategory,
  Organization,
} from "./types";

export interface DecisionPayload {
  userId: string;
  decision: ApprovalDecisionType;
  contentVersionHash: string;
}

export async function listPendingApprovals(
  organizationId: string
): Promise<ApprovalRequestDto[]> {
  return apiClient<ApprovalRequestDto[]>(
    `/api/v1/organizations/${organizationId}/approvals`
  );
}

export async function submitApprovalDecision(
  organizationId: string,
  approvalId: string,
  payload: DecisionPayload
): Promise<ApprovalDecisionDto> {
  return apiClient<ApprovalDecisionDto>(
    `/api/v1/organizations/${organizationId}/approvals/${approvalId}/decision`,
    {
      method: "POST",
      body: JSON.stringify(payload),
    }
  );
}

// Capability policy settings store (per organization)
const DEFAULT_CAPABILITY_POLICIES: Record<CapabilityCategory, ApprovalPolicySetting> = {
  WorktreeReadWrite: "Allow",
  LocalGit: "Allow",
  SandboxedCommand: "Allow",
  PackageNetwork: "Allow",
  ExternalNetwork: "Ask",
  DestructiveWorkspace: "Ask",
  SetupConfig: "Ask",
  RemoteGitWrite: "Ask",
  DraftDelivery: "Ask",
  PlatformDenied: "Deny", // PlatformDenied is strictly locked to Deny and cannot be overridden
};

export async function getCapabilityPolicies(
  organizationId: string
): Promise<Record<CapabilityCategory, ApprovalPolicySetting>> {
  if (typeof localStorage !== "undefined") {
    const key = `municlaw_policies_${organizationId}`;
    const raw = localStorage.getItem(key);
    if (raw) {
      try {
        const parsed = JSON.parse(raw);
        return {
          ...DEFAULT_CAPABILITY_POLICIES,
          ...parsed,
          PlatformDenied: "Deny", // Security invariant: cannot override PlatformDenied
        };
      } catch {
        // ignore parse error
      }
    }
  }
  return { ...DEFAULT_CAPABILITY_POLICIES };
}

export async function saveCapabilityPolicies(
  organizationId: string,
  policies: Record<CapabilityCategory, ApprovalPolicySetting>
): Promise<Record<CapabilityCategory, ApprovalPolicySetting>> {
  const safePolicies = {
    ...policies,
    PlatformDenied: "Deny" as ApprovalPolicySetting, // Invariant enforced
  };
  if (typeof localStorage !== "undefined") {
    localStorage.setItem(
      `municlaw_policies_${organizationId}`,
      JSON.stringify(safePolicies)
    );
  }
  return safePolicies;
}

export async function getOrganization(
  organizationId: string
): Promise<Organization> {
  return {
    id: organizationId,
    name: "Acme Engineering",
    slug: "acme-eng",
    createdAt: new Date().toISOString(),
  };
}
