import { TasksModule } from "@/modules/Tasks/TasksModule";

export function meta() {
  return [
    { title: "Tasks | MuniClaw Console" },
    { name: "description", content: "Autonomous coding task management" },
  ];
}

export default function TasksRoute() {
  return <TasksModule />;
}
