import { TaskDetailModule } from "@/modules/Tasks/TaskDetailModule";

export function meta() {
  return [
    { title: "Task Details | MuniClaw Console" },
    { name: "description", content: "Task activity, diffs, checks and execution" },
  ];
}

export default function TaskDetailRoute() {
  return <TaskDetailModule />;
}
