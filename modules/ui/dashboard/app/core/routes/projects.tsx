import { ProjectsModule } from "@/modules/Projects/ProjectsModule";

export function meta() {
  return [
    { title: "Projects | MuniClaw Console" },
    { name: "description", content: "Configured repository targets" },
  ];
}

export default function ProjectsRoute() {
  return <ProjectsModule />;
}
