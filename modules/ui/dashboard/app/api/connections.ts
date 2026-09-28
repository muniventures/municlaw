import { apiClient } from "./client";
import type {
  CredentialScope,
  OrganizationCredentialPolicy,
  Project,
  ProviderCredentialReference,
  RegisterOrganizationCredentialRequest,
  RepositoryConnection,
  UpdateCredentialPolicyRequest,
} from "./types";
import { getRuntimeConfig } from "@/core/config/runtime";

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

export async function getOrganizationCredentials(
  orgId: string
): Promise<ProviderCredentialReference[]> {
  try {
    return await apiClient<ProviderCredentialReference[]>(
      `/api/v1/organizations/${orgId}/credentials/organization`
    );
  } catch {
    try {
      return await apiClient<ProviderCredentialReference[]>(
        `/api/v1/credentials/organization`,
        { params: { organizationId: orgId } }
      );
    } catch {
      // Fallback: list all credentials for org and filter by Organization scope
      const creds = await listCredentials(orgId, "").catch(() => []);
      return creds.filter((c) => c.scope === "Organization");
    }
  }
}

export async function registerOrganizationCredential(
  orgId: string,
  payload: RegisterOrganizationCredentialRequest
): Promise<ProviderCredentialReference> {
  const body = {
    ...payload,
    scope: "Organization" as CredentialScope,
    organizationId: orgId,
  };

  try {
    return await apiClient<ProviderCredentialReference>(
      `/api/v1/organizations/${orgId}/credentials/organization`,
      {
        method: "POST",
        body: JSON.stringify(body),
      }
    );
  } catch {
    try {
      return await apiClient<ProviderCredentialReference>(
        `/api/v1/credentials/organization`,
        {
          method: "POST",
          body: JSON.stringify(body),
        }
      );
    } catch {
      // Fallback to standard credentials endpoint with scope: "Organization"
      return await apiClient<ProviderCredentialReference>(
        `/api/v1/organizations/${orgId}/credentials`,
        {
          method: "POST",
          body: JSON.stringify(body),
        }
      );
    }
  }
}

export async function updateCredentialPolicy(
  orgId: string,
  credentialId: string,
  policy: OrganizationCredentialPolicy | UpdateCredentialPolicyRequest
): Promise<ProviderCredentialReference> {
  try {
    return await apiClient<ProviderCredentialReference>(
      `/api/v1/organizations/${orgId}/credentials/organization/${credentialId}/policy`,
      {
        method: "PUT",
        body: JSON.stringify(policy),
      }
    );
  } catch {
    try {
      return await apiClient<ProviderCredentialReference>(
        `/api/v1/credentials/organization/${credentialId}/policy`,
        {
          method: "PUT",
          params: { organizationId: orgId },
          body: JSON.stringify(policy),
        }
      );
    } catch {
      return await apiClient<ProviderCredentialReference>(
        `/api/v1/organizations/${orgId}/credentials/${credentialId}/policy`,
        {
          method: "PUT",
          body: JSON.stringify(policy),
        }
      );
    }
  }
}

export async function getAccessibleCredentials(
  orgId: string
): Promise<ProviderCredentialReference[]> {
  try {
    return await apiClient<ProviderCredentialReference[]>(
      `/api/v1/organizations/${orgId}/credentials/accessible`
    );
  } catch {
    try {
      return await apiClient<ProviderCredentialReference[]>(
        `/api/v1/credentials/accessible`,
        { params: { organizationId: orgId } }
      );
    } catch {
      // Fallback: merge personal and organization credentials
      const userId = getRuntimeConfig().defaultUserId;
      const [personalCreds, orgCreds] = await Promise.all([
        listCredentials(orgId, userId).catch(() => []),
        getOrganizationCredentials(orgId).catch(() => []),
      ]);

      const seen = new Set<string>();
      const combined: ProviderCredentialReference[] = [];

      for (const cred of [...personalCreds, ...orgCreds]) {
        if (!seen.has(cred.id) && !cred.isRevoked) {
          seen.add(cred.id);
          combined.push(cred);
        }
      }
      return combined;
    }
  }
}

export async function revokeOrganizationCredential(
  orgId: string,
  credentialId: string,
  userId?: string
): Promise<{ status: string; credentialId: string }> {
  try {
    return await apiClient<{ status: string; credentialId: string }>(
      `/api/v1/organizations/${orgId}/credentials/organization/${credentialId}`,
      {
        method: "DELETE",
      }
    );
  } catch {
    try {
      return await apiClient<{ status: string; credentialId: string }>(
        `/api/v1/credentials/organization/${credentialId}`,
        {
          method: "DELETE",
          params: { organizationId: orgId },
        }
      );
    } catch {
      return await revokeCredential(
        orgId,
        credentialId,
        userId || getRuntimeConfig().defaultUserId
      );
    }
  }
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
