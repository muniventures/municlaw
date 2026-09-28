import * as React from "react";
import { GitConnections } from "./components/GitConnections";
import { CredentialKeyEntry } from "./components/CredentialKeyEntry";
import { OrganizationCredentialManager } from "./components/OrganizationCredentialManager";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import {
  listCredentials,
  getOrganizationCredentials,
  listRepositoryConnections,
} from "@/api/connections";
import type { ProviderCredentialReference, RepositoryConnection } from "@/api/types";
import { getRuntimeConfig } from "@/core/config/runtime";

export function ConnectionsModule() {
  const [config] = React.useState(() => getRuntimeConfig());
  const [personalCredentials, setPersonalCredentials] = React.useState<
    ProviderCredentialReference[]
  >([]);
  const [orgCredentials, setOrgCredentials] = React.useState<
    ProviderCredentialReference[]
  >([]);
  const [connections, setConnections] = React.useState<RepositoryConnection[]>([]);
  const [activeTab, setActiveTab] = React.useState("personal");
  const [isLoading, setIsLoading] = React.useState(true);

  const loadData = React.useCallback(async () => {
    try {
      setIsLoading(true);
      const [creds, sharedCreds, conns] = await Promise.all([
        listCredentials(config.defaultOrganizationId, config.defaultUserId).catch(() => []),
        getOrganizationCredentials(config.defaultOrganizationId).catch(() => []),
        listRepositoryConnections(config.defaultOrganizationId).catch(() => []),
      ]);

      setPersonalCredentials(creds.filter((c) => c.scope === "Personal"));

      const orgMap = new Map<string, ProviderCredentialReference>();
      for (const c of [...sharedCreds, ...creds.filter((x) => x.scope === "Organization")]) {
        orgMap.set(c.id, c);
      }
      setOrgCredentials(Array.from(orgMap.values()));
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

          <Tabs value={activeTab} onValueChange={setActiveTab} className="space-y-4">
            <TabsList className="grid w-full max-w-md grid-cols-2">
              <TabsTrigger value="personal">Personal API Keys</TabsTrigger>
              <TabsTrigger value="organization">Organization API Keys</TabsTrigger>
            </TabsList>

            <TabsContent value="personal">
              <CredentialKeyEntry
                organizationId={config.defaultOrganizationId}
                userId={config.defaultUserId}
                credentials={personalCredentials}
                onCredentialsUpdated={loadData}
              />
            </TabsContent>

            <TabsContent value="organization">
              <OrganizationCredentialManager
                organizationId={config.defaultOrganizationId}
                userId={config.defaultUserId}
                credentials={orgCredentials}
                onCredentialsUpdated={loadData}
              />
            </TabsContent>
          </Tabs>
        </div>
      )}
    </div>
  );
}

