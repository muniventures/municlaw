# Shared agent instructions

## Product and workflow

- MuniClaw is an independent product and repository. The initial hosted MVP is private, with a dedicated Minicloud-managed coding VPS per organization, GitHub/GitLab, BYOK and OpenCode.
- Never push, publish, change repository visibility, or deploy unless explicitly asked.
- Keep changes scoped; preserve unrelated work. Verify behavior from implementation and tests, not documentation alone.
- Read folder instructions before edits. Follow `features/SPEC_TEMPLATE.md`; feature folders contain an overview, `0.execution-plan.md`, and executable `subspecs/` with stable task IDs.
- Update feature status, `features/FEATURE_BACKLOG.md`, `features/FEATURES_REGISTRY.md`, and affected architecture docs with relevant changes.
- Use `scripts/temp/` for temporary scripts and remove them after use. Do not commit generated secrets or personal machine paths.
- Do not spawn parallel agents unless requested. Dependency/parallelization fields in plans describe scheduling only.

## Architecture conventions

- Target .NET 10, ASP.NET Core, EF Core/PostgreSQL, and xUnit, following the neighboring projects' conventions. Pin compatible packages when implementation starts.
- `modules/core` owns domain models, services, persistence, migrations, and application integration contracts. `modules/api` and `modules/worker` reference Core; neither references the other.
- Keep controllers thin and use DTOs rather than EF entities. Services own business rules and organization-scoped access. Use braces for C# control flow and cancellation tokens for I/O.
- Wrap explicit EF transactions in an execution strategy. Add migrations rather than modifying committed migrations.
- Use React 19, TypeScript, React Router 7, Vite, shadcn/ui, and lucide-react for the console. Keep routes thin, API calls centralized, and presentation within feature components.
- One shared task event/reconnect owner per frontend session; avoid parallel polling loops for the same task.
- MuniClaw owns its database and migrations. Access Minicloud only through authenticated APIs, never database access or sibling project references.
- No custom agent engine, Kubernetes, compatibility bridges, or extra service layers without a concrete requirement.
- OpenCode is an external pinned runtime dependency. Its permission system does not replace platform sandboxing.

## Public-repository hygiene

Only generic configuration examples belong in git. Do not copy internal infrastructure inventory, private SDKs, credentials, customer data, or production settings from neighboring repositories. Public source does not imply public hosted signup. License selection and actual repository visibility remain separate decisions.
