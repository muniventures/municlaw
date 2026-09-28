import { apiClient } from "./client";
import type { PreviewDeployment } from "./types";

export async function getPreview(
  taskId: string
): Promise<PreviewDeployment | null> {
  try {
    return await apiClient<PreviewDeployment>(`/api/v1/tasks/${taskId}/preview`);
  } catch (err: unknown) {
    if (
      err &&
      typeof err === "object" &&
      "status" in err &&
      (err as { status: number }).status === 404
    ) {
      return null;
    }
    throw err;
  }
}

export async function triggerPreview(
  taskId: string
): Promise<PreviewDeployment> {
  return apiClient<PreviewDeployment>(`/api/v1/tasks/${taskId}/preview`, {
    method: "POST",
  });
}

export async function tearDownPreview(
  taskId: string
): Promise<PreviewDeployment> {
  return apiClient<PreviewDeployment>(`/api/v1/tasks/${taskId}/preview`, {
    method: "DELETE",
  });
}
