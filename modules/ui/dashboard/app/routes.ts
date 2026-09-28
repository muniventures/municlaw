import { type RouteConfig, index, layout, route } from "@react-router/dev/routes";

export default [
  layout("./core/layout/AppShell.tsx", [
    index("./core/routes/home.tsx"),
    route("tasks", "./core/routes/tasks.tsx"),
    route("tasks/:taskId", "./core/routes/task-detail.tsx"),
    route("projects", "./core/routes/projects.tsx"),
    route("connections", "./core/routes/connections.tsx"),
    route("workspace", "./core/routes/workspace.tsx"),
    route("settings", "./core/routes/settings.tsx"),
    route("onboarding", "./core/routes/onboarding.tsx"),
  ]),
] satisfies RouteConfig;
