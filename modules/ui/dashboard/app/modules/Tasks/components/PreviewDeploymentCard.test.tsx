import { describe, it, expect, vi, beforeEach } from "vitest";
import * as React from "react";
import { renderToString } from "react-dom/server";
import { PreviewDeploymentCard } from "./PreviewDeploymentCard";
import type { PreviewDeployment } from "@/api/types";

describe("PreviewDeploymentCard", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  const basePreview: PreviewDeployment = {
    id: "prev-123",
    taskId: "task-999",
    runId: "run-001",
    branchName: "municlaw/task-999",
    normalizedBranch: "task-999",
    commitSha: "abc1234",
    previewUrl: "https://svc-task-999.app.muni.dev",
    status: "Active",
    createdAt: "2026-09-28T12:00:00Z",
    deployedAt: "2026-09-28T12:01:00Z",
    errorMessage: null,
  };

  it("renders 'Deploy Preview' button when no preview exists", () => {
    const html = renderToString(
      <PreviewDeploymentCard
        taskId="task-999"
        branchName="municlaw/task-999"
        initialPreview={null}
      />
    );

    expect(html).toContain("Preview Deployment");
    expect(html).toContain("Deploy Preview");
    expect(html).toContain("municlaw/task-999");
  });

  it("renders 'Deploy Preview' button when status is TornDown", () => {
    const tornDownPreview: PreviewDeployment = {
      ...basePreview,
      status: "TornDown",
      previewUrl: null,
    };

    const html = renderToString(
      <PreviewDeploymentCard
        taskId="task-999"
        branchName="municlaw/task-999"
        initialPreview={tornDownPreview}
      />
    );

    expect(html).toContain("Preview Deployment");
    expect(html).toContain("Deploy Preview");
    expect(html).toContain("Torn Down");
  });

  it("renders provisioning message and spinner when Deploying", () => {
    const deployingPreview: PreviewDeployment = {
      ...basePreview,
      status: "Deploying",
      previewUrl: null,
    };

    const html = renderToString(
      <PreviewDeploymentCard
        taskId="task-999"
        branchName="municlaw/task-999"
        initialPreview={deployingPreview}
      />
    );

    expect(html).toContain("Provisioning preview environment on Minicloud branch infrastructure...");
    expect(html).toContain("Deploying");
  });

  it("renders active pill, URL link, branch name, and 'Tear Down' button when Active", () => {
    const html = renderToString(
      <PreviewDeploymentCard
        taskId="task-999"
        branchName="municlaw/task-999"
        initialPreview={basePreview}
      />
    );

    expect(html).toContain("Live Preview");
    expect(html).toContain("https://svc-task-999.app.muni.dev");
    expect(html).toContain("municlaw/task-999");
    expect(html).toContain("Tear Down");
  });

  it("renders failure pill, error message, and 'Retry Deploy' button when Failed", () => {
    const failedPreview: PreviewDeployment = {
      ...basePreview,
      status: "Failed",
      previewUrl: null,
      errorMessage: "Domain provisioning DNS error: timeout reaching nameserver",
    };

    const html = renderToString(
      <PreviewDeploymentCard
        taskId="task-999"
        branchName="municlaw/task-999"
        initialPreview={failedPreview}
      />
    );

    expect(html).toContain("Failed");
    expect(html).toContain("Domain provisioning DNS error: timeout reaching nameserver");
    expect(html).toContain("Retry Deploy");
  });
});
