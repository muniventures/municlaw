import { apiClient } from "./client";
import type { DeliveryRecord, TaskEntity, TaskQueueStatus, TaskRun } from "./types";

export interface CreateTaskPayload {
  projectId: string;
  userId: string;
  title: string;
  baseBranch: string;
  baseCommitSha?: string;
  providerCredentialReferenceId: string;
  model: string;
  instruction: string;
  harnessVersion: string;
  maxBudgetUsd?: number;
  idempotencyKey?: string;
}

export interface FollowUpPayload {
  userId: string;
  instruction: string;
}

export interface CancelRunPayload {
  userId: string;
  reason?: string;
}

export interface PublishDeliveryPayload {
  runId: string;
  userId: string;
  baseCommitSha: string;
  reviewedCommitSha: string;
  targetBranch: string;
  title: string;
  body: string;
}

export async function listTasks(organizationId: string): Promise<TaskEntity[]> {
  return apiClient<TaskEntity[]>(`/api/v1/organizations/${organizationId}/tasks`);
}

export async function getTask(
  organizationId: string,
  taskId: string
): Promise<TaskEntity> {
  return apiClient<TaskEntity>(
    `/api/v1/organizations/${organizationId}/tasks/${taskId}`
  );
}

export async function createTask(
  organizationId: string,
  payload: CreateTaskPayload
): Promise<{ task: TaskEntity; initialRun: TaskRun }> {
  return apiClient<{ task: TaskEntity; initialRun: TaskRun }>(
    `/api/v1/organizations/${organizationId}/tasks`,
    {
      method: "POST",
      body: JSON.stringify(payload),
    }
  );
}

export async function createFollowUp(
  organizationId: string,
  taskId: string,
  payload: FollowUpPayload
): Promise<TaskRun> {
  return apiClient<TaskRun>(
    `/api/v1/organizations/${organizationId}/tasks/${taskId}/followup`,
    {
      method: "POST",
      body: JSON.stringify(payload),
    }
  );
}

export async function cancelRun(
  organizationId: string,
  taskId: string,
  runId: string,
  payload: CancelRunPayload
): Promise<{ status: string; runId: string }> {
  return apiClient<{ status: string; runId: string }>(
    `/api/v1/organizations/${organizationId}/tasks/${taskId}/runs/${runId}/cancel`,
    {
      method: "POST",
      body: JSON.stringify(payload),
    }
  );
}

export async function publishDelivery(
  organizationId: string,
  taskId: string,
  payload: PublishDeliveryPayload
): Promise<DeliveryRecord> {
  return apiClient<DeliveryRecord>(
    `/api/v1/organizations/${organizationId}/tasks/${taskId}/deliver`,
    {
      method: "POST",
      body: JSON.stringify(payload),
    }
  );
}

export async function getDelivery(
  organizationId: string,
  taskId: string
): Promise<DeliveryRecord | null> {
  try {
    return await apiClient<DeliveryRecord>(
      `/api/v1/organizations/${organizationId}/tasks/${taskId}/delivery`
    );
  } catch (err: unknown) {
    if (err && typeof err === "object" && "status" in err && (err as { status: number }).status === 404) {
      return null;
    }
    throw err;
  }
}

export async function getTaskQueueStatus(
  taskId: string
): Promise<TaskQueueStatus> {
  return apiClient<TaskQueueStatus>(`/api/v1/tasks/${taskId}/queue`);
}

