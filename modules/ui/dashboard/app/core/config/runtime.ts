export interface RuntimeConfig {
  apiBaseUrl: string;
  appName: string;
  domain: string;
  defaultOrganizationId: string;
  defaultUserId: string;
}

const DEFAULT_ORG_ID = "00000000-0000-0000-0000-000000000001";
const DEFAULT_USER_ID = "00000000-0000-0000-0000-000000000002";

export function getRuntimeConfig(): RuntimeConfig {
  const publicApiUrl = typeof window !== "undefined"
    ? (window as unknown as { __PUBLIC_CONFIG__?: { apiBaseUrl?: string } }).__PUBLIC_CONFIG__?.apiBaseUrl
    : undefined;

  return {
    apiBaseUrl: publicApiUrl || (typeof import.meta !== "undefined" ? (import.meta.env.VITE_API_BASE_URL || "") : ""),
    appName: "MuniClaw Console",
    domain: "ai.muni.dev",
    defaultOrganizationId: typeof localStorage !== "undefined"
      ? localStorage.getItem("municlaw_org_id") || DEFAULT_ORG_ID
      : DEFAULT_ORG_ID,
    defaultUserId: typeof localStorage !== "undefined"
      ? localStorage.getItem("municlaw_user_id") || DEFAULT_USER_ID
      : DEFAULT_USER_ID,
  };
}

export function saveActiveOrgId(orgId: string): void {
  if (typeof localStorage !== "undefined") {
    localStorage.setItem("municlaw_org_id", orgId);
  }
}

export function saveActiveUserId(userId: string): void {
  if (typeof localStorage !== "undefined") {
    localStorage.setItem("municlaw_user_id", userId);
  }
}
