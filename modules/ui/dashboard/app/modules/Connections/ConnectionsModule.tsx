import * as React from "react";
import { GitConnections } from "./components/GitConnections";
import { CredentialKeyEntry } from "./components/CredentialKeyEntry";
import { listCredentials, listRepositoryConnections } from "@/api/connections";
import type { ProviderCredentialReference, RepositoryConnection } from "@/api/types";
import { getRuntimeConfig } from "@/core/config/runtime";

export function ConnectionsModule() {
  const [config] = React.useState(() => getRuntimeConfig());
  const [credentials, setCredentials] = React.useState<ProviderCredentialReference[]>([]);
  const [connections, setConnections] = React.useState<RepositoryConnection[]>([]);
  const [isLoading, setIsLoading] = React.useState(true);

  const loadData = React.useCallback(async () => {
    try {
      setIsLoading(true);
      const [creds, conns] = await Promise.all([
        listCredentials(config.defaultOrganizationId, config.defaultUserId).catch(() => []),
        listRepositoryConnections(config.defaultOrganizationId).catch(() => []),
      ]);
      setCredentials(creds);
      setConnections(conns);
    } finally {
      setIsLoading(false);
    }
  }, [config.defaultOrganizationId, config.defaultUserId]);

  React.useEffect(() => {
    loadData();
  }, [loadData]);

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight text-foreground">
          Connections & BYOK Credentials
        </h1>
        <p className="text-sm text-muted-foreground mt-1">
          Manage your GitHub/GitLab integrations and register encrypted, write-only AI provider API keys.
        </p>
      </div>

      {isLoading ? (
        <div className="p-8 text-center text-muted-foreground font-mono text-xs">
          Loading connection credentials...
        </div>
      ) : (
        <div className="space-y-6">
          <GitConnections connections={connections} />
          <CredentialKeyEntry
            organizationId={config.defaultOrganizationId}
            userId={config.defaultUserId}
            credentials={credentials}
            onCredentialsUpdated={loadData}
          />
        </div>
      )}
    </div>
  );
}
