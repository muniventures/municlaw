# Data and integrations

MuniClaw owns a PostgreSQL database and EF migrations. Task/run/event, project, repository connection, approvals, delivery, usage and workspace linkage are organization-scoped. API services are the only database access boundary; workers call scoped APIs.

## Minicloud

Define a versioned authenticated infrastructure adapter for provision/status/delete and ownership linkage. Minicloud remains authoritative for infrastructure; MuniClaw for coding tasks. Required endpoints must be verified or implemented in Minicloud before claiming integration. Never import private source, share databases, or assume an existing general-purpose provisioning API.

Hosted identity can use the same Firebase project with explicit audiences/allowlisting and membership validation. Organization synchronization contract is an implementation gate; never trust a browser-supplied organization ID alone.

## Git and models

GitHub.com uses installation access; GitLab.com uses OAuth. Verify scopes, redirect validation, token rotation, webhook signatures and revocation against current official docs when implementing. Trusted helpers handle credentialed Git operations and reviewed draft PR/MR publication.

OpenCode is the first harness for BYOK OpenAI, Anthropic and DeepSeek. Long-lived provider/Git keys belong in a scoped secret store; the hosted adapter may use a Minicloud-authorized secret API backed by OpenBao. No direct access to Minicloud's database or root vault token. Self-hosted secret storage is a future adapter, not an implemented capability.

A trusted model proxy must be qualified so shell processes cannot read provider keys. Persist provider-reported usage with clear estimation provenance; provider bills are authoritative.

Messaging, native mobile, preview deployment and standalone self-host installation are deferred. Future adapters must not require private Minicloud source to build the public repository.
