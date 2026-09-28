import { describe, it, expect, vi, beforeEach } from "vitest";
import { getPreview, triggerPreview, tearDownPreview } from "./preview";
import type { PreviewDeployment } from "./types";

describe("Preview API client", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  const mockPreview: PreviewDeployment = {
    id: "prev-123",
    taskId: "task-456",
    runId: "run-789",
    branchName: "municlaw/task-456",
    normalizedBranch: "task-456",
    commitSha: "c0ffee1",
    previewUrl: "https://web-task-456.app.muni.dev",
    status: "Active",
    createdAt: "2026-09-28T12:00:00Z",
    deployedAt: "2026-09-28T12:01:00Z",
    errorMessage: null,
  };

  it("fetches preview deployment successfully", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue({
      ok: true,
      status: 200,
      json: async () => mockPreview,
    } as Response);

    const result = await getPreview("task-456");
    expect(result).toEqual(mockPreview);
    expect(result?.status).toBe("Active");
    expect(result?.previewUrl).toBe("https://web-task-456.app.muni.dev");
  });

  it("returns null when getPreview returns 404", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue({
      ok: false,
      status: 404,
      statusText: "Not Found",
      json: async () => ({ error: "not_found", message: "Preview not found" }),
    } as Response);

    const result = await getPreview("task-456");
    expect(result).toBeNull();
  });

  it("throws ApiError when getPreview fails with non-404 error", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue({
      ok: false,
      status: 500,
      statusText: "Internal Server Error",
      json: async () => ({ error: "server_error", message: "Internal server error" }),
    } as Response);

    await expect(getPreview("task-456")).rejects.toThrow("Internal server error");
  });

  it("triggers preview deployment with POST method", async () => {
    let capturedUrl: string | undefined;
    let capturedMethod: string | undefined;

    vi.spyOn(globalThis, "fetch").mockImplementation(async (url, init) => {
      capturedUrl = String(url);
      capturedMethod = init?.method;
      return {
        ok: true,
        status: 200,
        json: async () => ({
          ...mockPreview,
          status: "Deploying",
          previewUrl: null,
        }),
      } as Response;
    });

    const result = await triggerPreview("task-456");
    expect(capturedUrl).toContain("/api/v1/tasks/task-456/preview");
    expect(capturedMethod).toBe("POST");
    expect(result.status).toBe("Deploying");
  });

  it("tears down preview deployment with DELETE method", async () => {
    let capturedUrl: string | undefined;
    let capturedMethod: string | undefined;

    vi.spyOn(globalThis, "fetch").mockImplementation(async (url, init) => {
      capturedUrl = String(url);
      capturedMethod = init?.method;
      return {
        ok: true,
        status: 200,
        json: async () => ({
          ...mockPreview,
          status: "TornDown",
        }),
      } as Response;
    });

    const result = await tearDownPreview("task-456");
    expect(capturedUrl).toContain("/api/v1/tasks/task-456/preview");
    expect(capturedMethod).toBe("DELETE");
    expect(result.status).toBe("TornDown");
  });
});
