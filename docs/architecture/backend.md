# Backend

Target architecture; implementation pending T00 qualification.

- `modules/api/MuniClaw.Api.csproj`: ASP.NET Core HTTP host, Controllers, DTOs, Auth, request validation and composition.
- `modules/core/MuniClaw.Core.csproj`: Models, Contracts, Services, DatabaseContexts, Migrations, Integrations, and Validation.
- `modules/worker/MuniClaw.Worker.csproj`: independent supervisor host, runtime operations and harness/sandbox adapters.
- `modules/tests/MuniClaw.Tests` and `MuniClaw.Worker.Tests`: xUnit projects once implementation begins.
- `MuniClaw.slnx`: add with the first runnable .NET projects, not an empty solution.

Dependency direction is API -> Core and Worker -> Core. Core must not depend on either host. Worker configuration must not contain control-plane database credentials; shared model code does not grant database access. Worker claims/results flow through scoped APIs. Only the control plane accesses its PostgreSQL database and owns migrations.

Services enforce organization ownership and separate Task/TaskRun transitions. Persist runs, replayable events, approvals, delivery and usage independently from harness sessions. Browser event delivery uses SSE with monotonic cursor replay. Worker dispatch uses outbound HTTP long-polling, leased claims, fencing tokens, heartbeat command cursors, and acknowledged idempotent event/result batches. Never retry uncertain Git publication blindly.

The separate frontend uses a same-origin `/api` reverse-proxy route. Authentication issuer/audience and MuniClaw-owned organization membership are verified server-side. The MVP permits one active membership per organization through policy while retaining a multi-user data model. Minicloud transport stays behind an integration interface; no private repo references or shared database schema.
