import { apiClient } from "./client";
import type {
  CreateNotificationChannelRequest,
  NotificationChannel,
  UpdateNotificationChannelRequest,
} from "./types";

export async function listChannels(
  orgId: string
): Promise<NotificationChannel[]> {
  return apiClient<NotificationChannel[]>(
    `/api/v1/organizations/${orgId}/notifications/channels`
  );
}

export async function createChannel(
  orgId: string,
  req: CreateNotificationChannelRequest
): Promise<NotificationChannel> {
  return apiClient<NotificationChannel>(
    `/api/v1/organizations/${orgId}/notifications/channels`,
    {
      method: "POST",
      body: JSON.stringify(req),
    }
  );
}

export async function updateChannel(
  orgId: string,
  channelId: string,
  req: UpdateNotificationChannelRequest
): Promise<NotificationChannel> {
  return apiClient<NotificationChannel>(
    `/api/v1/organizations/${orgId}/notifications/channels/${channelId}`,
    {
      method: "PUT",
      body: JSON.stringify(req),
    }
  );
}

export async function deleteChannel(
  orgId: string,
  channelId: string
): Promise<void> {
  return apiClient<void>(
    `/api/v1/organizations/${orgId}/notifications/channels/${channelId}`,
    {
      method: "DELETE",
    }
  );
}

export async function sendTestPing(
  orgId: string,
  channelId: string
): Promise<{ success: boolean; message: string }> {
  try {
    return await apiClient<{ success: boolean; message: string }>(
      `/api/v1/organizations/${orgId}/notifications/channels/${channelId}/test`,
      {
        method: "POST",
      }
    );
  } catch (err: unknown) {
    if (err && typeof err === "object" && "status" in err && (err as { status: number }).status === 404) {
      return await apiClient<{ success: boolean; message: string }>(
        `/api/v1/organizations/${orgId}/notifications/channels/${channelId}/ping`,
        {
          method: "POST",
        }
      );
    }
    throw err;
  }
}
