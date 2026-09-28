import { describe, it, expect, vi, beforeEach } from "vitest";
import {
  listChannels,
  createChannel,
  updateChannel,
  deleteChannel,
  sendTestPing,
} from "./notifications";
import type {
  CreateNotificationChannelRequest,
  NotificationChannel,
  UpdateNotificationChannelRequest,
} from "./types";

describe("Notifications API client", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  const mockChannel: NotificationChannel = {
    id: "chan-123",
    organizationId: "org-1",
    channelType: "Slack",
    name: "#dev-notifications",
    maskedWebhookUrl: "https://hooks.slack.com/services/T00/B00/****",
    subscribedEvents: ["ApprovalRequested", "TaskCompleted", "TaskFailed"],
    isEnabled: true,
    createdAt: "2026-09-28T12:00:00Z",
    lastDispatchedAt: "2026-09-28T14:00:00Z",
    lastDispatchStatus: "Success",
  };

  it("lists notification channels for an organization", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => [mockChannel],
    } as Response);

    const channels = await listChannels("org-1");
    expect(channels).toHaveLength(1);
    expect(channels[0].id).toBe("chan-123");
    expect(channels[0].name).toBe("#dev-notifications");
    expect(channels[0].channelType).toBe("Slack");
    expect(channels[0].subscribedEvents).toContain("ApprovalRequested");
  });

  it("creates a new notification channel with payload", async () => {
    let capturedBody: any;
    let capturedUrl: string | undefined;

    vi.spyOn(globalThis, "fetch").mockImplementation(async (url, init) => {
      capturedUrl = String(url);
      if (init?.body) {
        capturedBody = JSON.parse(init.body as string);
      }
      return {
        ok: true,
        status: 201,
        json: async () => ({
          ...mockChannel,
          id: "chan-456",
          name: capturedBody.name,
          channelType: capturedBody.channelType,
          subscribedEvents: capturedBody.subscribedEvents,
        }),
      } as Response;
    });

    const req: CreateNotificationChannelRequest = {
      name: "Discord Builds",
      channelType: "Discord",
      webhookUrl: "https://discord.com/api/webhooks/123/xyz",
      subscribedEvents: ["TaskCompleted", "DeliveryPublished"],
      isEnabled: true,
    };

    const created = await createChannel("org-1", req);
    expect(capturedUrl).toContain("/api/v1/organizations/org-1/notifications/channels");
    expect(capturedBody.name).toBe("Discord Builds");
    expect(capturedBody.channelType).toBe("Discord");
    expect(capturedBody.webhookUrl).toBe("https://discord.com/api/webhooks/123/xyz");
    expect(capturedBody.subscribedEvents).toEqual(["TaskCompleted", "DeliveryPublished"]);
    expect(created.name).toBe("Discord Builds");
    expect(created.channelType).toBe("Discord");
  });

  it("creates a generic webhook channel with signing secret", async () => {
    let capturedBody: any;
    vi.spyOn(globalThis, "fetch").mockImplementation(async (_url, init) => {
      if (init?.body) {
        capturedBody = JSON.parse(init.body as string);
      }
      return {
        ok: true,
        status: 201,
        json: async () => ({
          ...mockChannel,
          id: "chan-789",
          name: capturedBody.name,
          channelType: capturedBody.channelType,
        }),
      } as Response;
    });

    const req: CreateNotificationChannelRequest = {
      name: "Custom Webhook",
      channelType: "GenericWebhook",
      webhookUrl: "https://example.com/webhook",
      subscribedEvents: ["ApprovalRequested", "TaskFailed"],
      signingSecret: "super-secret-hmac-key",
      isEnabled: true,
    };

    const created = await createChannel("org-1", req);
    expect(capturedBody.signingSecret).toBe("super-secret-hmac-key");
    expect(capturedBody.channelType).toBe("GenericWebhook");
    expect(created.name).toBe("Custom Webhook");
  });

  it("updates an existing notification channel", async () => {
    let capturedBody: any;
    let capturedMethod: string | undefined;

    vi.spyOn(globalThis, "fetch").mockImplementation(async (_url, init) => {
      capturedMethod = init?.method;
      if (init?.body) {
        capturedBody = JSON.parse(init.body as string);
      }
      return {
        ok: true,
        status: 200,
        json: async () => ({
          ...mockChannel,
          name: capturedBody.name ?? mockChannel.name,
          isEnabled: capturedBody.isEnabled ?? mockChannel.isEnabled,
        }),
      } as Response;
    });

    const updateReq: UpdateNotificationChannelRequest = {
      name: "Renamed Slack Channel",
      isEnabled: false,
    };

    const updated = await updateChannel("org-1", "chan-123", updateReq);
    expect(capturedMethod).toBe("PUT");
    expect(capturedBody.name).toBe("Renamed Slack Channel");
    expect(capturedBody.isEnabled).toBe(false);
    expect(updated.name).toBe("Renamed Slack Channel");
    expect(updated.isEnabled).toBe(false);
  });

  it("deletes a notification channel", async () => {
    let capturedMethod: string | undefined;
    let capturedUrl: string | undefined;

    vi.spyOn(globalThis, "fetch").mockImplementation(async (url, init) => {
      capturedUrl = String(url);
      capturedMethod = init?.method;
      return {
        ok: true,
        status: 204,
      } as Response;
    });

    await deleteChannel("org-1", "chan-123");
    expect(capturedMethod).toBe("DELETE");
    expect(capturedUrl).toContain("/api/v1/organizations/org-1/notifications/channels/chan-123");
  });

  it("sends a test ping successfully", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => ({ success: true, message: "Test notification delivered to Slack" }),
    } as Response);

    const result = await sendTestPing("org-1", "chan-123");
    expect(result.success).toBe(true);
    expect(result.message).toContain("Test notification delivered");
  });

  it("falls back to /ping endpoint when /test returns 404", async () => {
    vi.spyOn(globalThis, "fetch").mockImplementation(async (url) => {
      const urlStr = String(url);
      if (urlStr.endsWith("/test")) {
        return {
          ok: false,
          status: 404,
          statusText: "Not Found",
          json: async () => ({ error: "not_found" }),
        } as Response;
      }
      if (urlStr.endsWith("/ping")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({ success: true, message: "Ping accepted" }),
        } as Response;
      }
      return {
        ok: false,
        status: 500,
        json: async () => ({}),
      } as Response;
    });

    const result = await sendTestPing("org-1", "chan-123");
    expect(result.success).toBe(true);
    expect(result.message).toBe("Ping accepted");
  });
});
