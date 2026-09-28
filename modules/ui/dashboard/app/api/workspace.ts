import { apiClient } from "./client";
import type { CodingWorkspace } from "./types";

export interface RequestWorkspacePayload {
  userId: string;
  region?: string;
}

export async function requestWorkspace(
  organizationId: string,
  payload: RequestWorkspacePayload
): Promise<CodingWorkspace> {
  return apiClient<CodingWorkspace>(
    `/api/v1/organizations/${organizationId}/workspace`,
    {
      method: "POST",
      body: JSON.stringify(payload),
    }
  );
}

export async function getWorkspaceStatus(
  organizationId: string,
  userId: string
): Promise<CodingWorkspace | null> {
  try {
    return await apiClient<CodingWorkspace>(
      `/api/v1/organizations/${organizationId}/workspace`,
      {
        params: { userId },
      }
    );
  } catch (err: unknown) {
    if (err && typeof err === "object" && "status" in err && (err as { status: number }).status === 404) {
      return null;
    }
    throw err;
  }
}

export async function deleteWorkspace(
  organizationId: string,
  userId: string
): Promise<{ status: string; organizationId: string }> {
  return apiClient<{ status: string; organizationId: string }>(
    `/api/v1/organizations/${organizationId}/workspace`,
    {
      method: "DELETE",
      params: { userId },
    }
  );
}
