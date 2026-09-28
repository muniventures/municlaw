import { describe, it, expect } from "vitest";
import type {
  CreateNotificationChannelRequest,
  NotificationChannelType,
  NotificationEventType,
} from "@/api/types";

describe("NotificationChannels validation and helper logic", () => {
  function validateChannelInput(
    mode: "add" | "edit",
    name: string,
    webhookUrl: string,
    subscribedEvents: NotificationEventType[]
  ): string | null {
    if (!name.trim()) {
      return "Channel name is required.";
    }
    if (mode === "add" && !webhookUrl.trim()) {
      return "Webhook URL is required.";
    }
    if (webhookUrl.trim() && !/^https?:\/\/.+/i.test(webhookUrl.trim())) {
      return "Webhook URL must start with http:// or https://";
    }
    if (subscribedEvents.length === 0) {
      return "Please select at least one subscribable event.";
    }
    return null;
  }

  function generateSigningSecret(): string {
    const array = new Uint8Array(24);
    crypto.getRandomValues(array);
    return Array.from(array, (byte) => byte.toString(16).padStart(2, "0")).join("");
  }

  it("validates channel name presence", () => {
    const err = validateChannelInput("add", "   ", "https://hooks.slack.com", ["TaskCompleted"]);
    expect(err).toBe("Channel name is required.");
  });

  it("validates webhook URL presence on add", () => {
    const err = validateChannelInput("add", "Dev Slack", "", ["TaskCompleted"]);
    expect(err).toBe("Webhook URL is required.");
  });

  it("allows blank webhook URL on edit (write-only preservation)", () => {
    const err = validateChannelInput("edit", "Dev Slack", "", ["TaskCompleted"]);
    expect(err).toBeNull();
  });

  it("validates URL protocol format", () => {
    const err = validateChannelInput("add", "Dev Slack", "ftp://invalid-url", ["TaskCompleted"]);
    expect(err).toBe("Webhook URL must start with http:// or https://");
  });

  it("validates that at least one event is subscribed", () => {
    const err = validateChannelInput("add", "Dev Slack", "https://hooks.slack.com", []);
    expect(err).toBe("Please select at least one subscribable event.");
  });

  it("accepts valid inputs for channel creation", () => {
    const err = validateChannelInput(
      "add",
      "Dev Slack",
      "https://hooks.slack.com/services/T00/B00/XXXX",
      ["ApprovalRequested", "TaskCompleted", "TaskFailed", "DeliveryPublished"]
    );
    expect(err).toBeNull();
  });

  it("generates a high-entropy 48-char hex signing secret for generic webhooks", () => {
    const secret = generateSigningSecret();
    expect(secret).toMatch(/^[0-9a-f]{48}$/);
    const secret2 = generateSigningSecret();
    expect(secret).not.toBe(secret2);
  });
});
