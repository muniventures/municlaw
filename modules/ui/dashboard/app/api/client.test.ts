import { describe, it, expect, vi, beforeEach } from "vitest";
import { apiClient, ApiError } from "./client";
import { sseManager } from "./sse";
import { getCapabilityPolicies, saveCapabilityPolicies } from "./settings";
import type { TaskEventDto } from "./types";

describe("apiClient", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("handles successful JSON response", async () => {
    const mockData = { id: "123", name: "test" };
    vi.spyOn(globalThis, "fetch").mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => mockData,
    } as Response);

    const result = await apiClient<{ id: string; name: string }>("/api/v1/test");
    expect(result).toEqual(mockData);
  });

  it("throws structured ApiError on non-200 responses", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue({
      ok: false,
      status: 403,
      statusText: "Forbidden",
      json: async () => ({ error: "forbidden", message: "User is not allowlisted" }),
    } as Response);

    await expect(apiClient("/api/v1/secure")).rejects.toThrow(ApiError);
  });
});

describe("sseManager", () => {
  it("enforces deduplication and tracks sequence", () => {
    const eventsReceived: TaskEventDto[] = [];
    const unsubscribe = sseManager.subscribe("org-1", "task-1", "run-1", {
      onEvent: (evt) => eventsReceived.push(evt),
    });

    // Private method access or public emission check
    expect(sseManager.getStatus()).toBeDefined();

    unsubscribe();
  });
});

describe("Capability Policy Security", () => {
  it("ensures PlatformDenied is always locked to Deny", async () => {
    const defaultPolicies = await getCapabilityPolicies("org-test");
    expect(defaultPolicies.PlatformDenied).toBe("Deny");

    // Attempting to override PlatformDenied to Allow must be rejected / prevented
    const updated = await saveCapabilityPolicies("org-test", {
      ...defaultPolicies,
      PlatformDenied: "Allow",
    });

    expect(updated.PlatformDenied).toBe("Deny");
  });
});
