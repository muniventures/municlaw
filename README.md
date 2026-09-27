# MuniClaw

Cloud coding workspaces powered by existing agent harnesses, starting with OpenCode. Connect GitHub or GitLab, bring your model API keys, and review changes from a separate web console.

## Status

Repository foundation and MVP specification only. No application, harness integration, or deployment is implemented yet. The hosted MVP is private; `ai.muni.dev` is the proposed console hostname. Repository visibility is managed separately on GitHub. A distribution license has not yet been selected.

## Repository structure

```text
modules/
  api/                  ASP.NET Core HTTP API and authentication
  core/                 Domain, services, data, migrations, integration contracts
  worker/               VPS supervisor, sandbox lifecycle, harness adapters
  ui/dashboard/         Independent React console
  tests/                Backend and worker test projects
features/               Specifications, execution plans, backlog and registry
docs/architecture/      Architecture and shared contributor/agent instructions
infra/                  Deployment and sandbox configuration
scripts/                Repository tooling
```

The module folders currently contain implementation guidance, not runnable projects. See the [architecture index](docs/architecture/README.md), [MVP specification](features/product/municlaw/1.municlaw.md), and [execution plan](features/product/municlaw/0.execution-plan.md). Start implementation with T00 harness qualification.

## Minicloud boundary

MuniClaw is independently versioned and deployed. Minicloud manages infrastructure through authenticated service APIs. MuniClaw owns its database, task domain, Git connections, and coding runtime. No sibling-repository project references, private source dependencies, or shared database access are allowed. Managed sign-in may use the same identity provider while each product enforces its own authorization.

Self-hosting is a design objective, not an available installation flow. The managed MVP uses Minicloud; a standalone infrastructure adapter and installation guide are future work.

## Contributing

Read [CONTRIBUTING.md](CONTRIBUTING.md) and [AGENTS.md](AGENTS.md). Never commit credentials or production configuration. No push or deployment is part of repository setup.
