# Frontend

Target: `modules/ui/dashboard`, independently deployed at proposed `ai.muni.dev`.

```text
app/
  root.tsx
  routes.ts
  core/routes/           Thin route entry points
  core/config/           Public runtime configuration
  api/                   Shared HTTP client and feature API clients
  modules/<Feature>/
    <Feature>Module.tsx  Feature orchestration
    components/          Presentational components
  components/ui/         shadcn primitives
```

Use React 19, TypeScript, React Router 7, Vite, shadcn/ui and lucide-react. Adopt the neighboring projects' route -> module -> component pattern, without copying private application code. API configuration and task event reconnection have shared owners. Task activity uses one SSE connection owner with cursor replay and deduplication. Feature modules use the shared client rather than raw duplicated fetch/auth logic.

Navigation: Tasks, Projects, Connections, Workspace, Settings. Responsive web is MVP; native mobile and messaging are later. Show actual test evidence, unknown usage, offline workers, and stale approvals accurately. Do not ship fake successful agent activity.

Shared identity does not mean a shared console. This application has its own build, navigation, onboarding, authorization and deployment. No direct browser access to OpenCode or private worker endpoints. Only public values may enter frontend environment variables.
