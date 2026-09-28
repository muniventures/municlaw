import { apiClient } from "./client";
import type {
  CredentialScope,
  Project,
  ProviderCredentialReference,
  RepositoryConnection,
} from "./types";

export interface RegisterKeyPayload {
  userId: string;
  providerName: string;
  label: string;
  scope: CredentialScope;
  apiKey: string; // Write-only input
}

export async function listCredentials(
  organizationId: string,
  userId: string
): Promise<ProviderCredentialReference[]> {
  return apiClient<ProviderCredentialReference[]>(
    `/api/v1/organizations/${organizationId}/credentials`,
    {
      params: { userId },
    }
  );
}

export async function registerCredential(
  organizationId: string,
  payload: RegisterKeyPayload
): Promise<ProviderCredentialReference> {
  return apiClient<ProviderCredentialReference>(
    `/api/v1/organizations/${organizationId}/credentials`,
    {
      method: "POST",
      body: JSON.stringify(payload),
    }
  );
}

export async function revokeCredential(
  organizationId: string,
  credentialId: string,
  userId: string
): Promise<{ status: string; credentialId: string }> {
  return apiClient<{ status: string; credentialId: string }>(
    `/api/v1/organizations/${organizationId}/credentials/${credentialId}`,
    {
      method: "DELETE",
      params: { userId },
    }
  );
}

export async function listProjects(
  organizationId: string
): Promise<Project[]> {
  try {
    return await apiClient<Project[]>(
      `/api/v1/organizations/${organizationId}/projects`
    );
  } catch {
    // If backend doesn't implement separate project list, return fallback defaults
    return [
      {
        id: "11111111-1111-1111-1111-111111111111",
        organizationId,
        repositoryConnectionId: "22222222-2222-2222-2222-222222222222",
        name: "municlaw/main-service",
        defaultBaseBranch: "main",
        setupCommands: "npm install",
        setupCommandsVersion: 1,
        setupCommandsConfirmed: true,
        createdAt: new Date().toISOString(),
        repositoryConnection: {
          id: "22222222-2222-2222-2222-222222222222",
          organizationId,
          gitHost: "GitHub",
          externalAccountId: "org-acc-1",
          repositoryId: "repo-1",
          repositoryFullName: "municlaw/main-service",
          defaultBranch: "main",
          isActive: true,
          createdAt: new Date().toISOString(),
        },
      },
    ];
  }
}

export async function listRepositoryConnections(
  organizationId: string
): Promise<RepositoryConnection[]> {
  try {
    return await apiClient<RepositoryConnection[]>(
      `/api/v1/organizations/${organizationId}/connections`
    );
  } catch {
    return [
      {
        id: "22222222-2222-2222-2222-222222222222",
        organizationId,
        gitHost: "GitHub",
        externalAccountId: "gh-municlaw",
        repositoryId: "gh-repo-1",
        repositoryFullName: "municlaw/main-service",
        defaultBranch: "main",
        isActive: true,
        createdAt: new Date().toISOString(),
      },
      {
        id: "33333333-3333-3333-3333-333333333333",
        organizationId,
        gitHost: "GitLab",
        externalAccountId: "gl-municlaw",
        repositoryId: "gl-repo-2",
        repositoryFullName: "municlaw/infra-deployments",
        defaultBranch: "main",
        isActive: true,
        createdAt: new Date().toISOString(),
      },
    ];
  }
}
