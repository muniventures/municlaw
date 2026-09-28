# Data and integrations

MuniClaw owns a PostgreSQL database and EF migrations. Task/run/event, project, repository connection, approvals, delivery, usage and workspace linkage are organization-scoped. API services are the only database access boundary; workers call scoped APIs.

## Minicloud

Define a versioned authenticated infrastructure adapter for provision/status/delete and ownership linkage. MuniClaw is authoritative for its organizations and memberships; Minicloud remains authoritative for infrastructure. MuniClaw creates the scoped service identity or linkage needed to operate infrastructure for each organization. Required endpoints must be verified or implemented in Minicloud before claiming integration. Never import private source, share databases, or assume an existing general-purpose provisioning API.

Hosted authentication can use the same Firebase project with explicit audiences and allowlisting, but external identity claims do not define MuniClaw organization membership. MuniClaw maps authenticated subjects to its own users and memberships and validates them server-side; never trust a browser-supplied organization ID alone. The MVP enforces one active membership per organization without encoding that limit into the relational model.

## Git and models

GitHub.com uses installation access and may attribute publication to the GitHub App; GitLab.com uses OAuth and may attribute publication to the connected user. Verify scopes, redirect validation, token rotation, webhook signatures and revocation against current official docs when implementing. Trusted helpers handle credentialed Git operations and reviewed draft PR/MR publication. Publication is bound to the reviewed repository/base/target/commit tuple. A moved remote task branch stops publication for reconciliation rather than triggering overwrite, automatic rebase or force-push.

OpenCode is the first harness for BYOK OpenAI, Anthropic and DeepSeek. Long-lived provider/Git keys belong in a scoped secret store; the hosted adapter may use a Minicloud-authorized secret API backed by OpenBao. Provider credential metadata supports personal and organization scopes, but MVP creation and use are personal-only. No direct access to Minicloud's database or root vault token. Self-hosted secret storage is a future adapter, not an implemented capability.

A trusted model proxy must be qualified so shell processes cannot read provider keys. Persist provider-reported usage with clear estimation provenance; provider bills are authoritative.

Messaging, native mobile, preview deployment and standalone self-host installation are deferred. Future adapters must not require private Minicloud source to build the public repository.
