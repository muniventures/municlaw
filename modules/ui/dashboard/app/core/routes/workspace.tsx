import { WorkspaceModule } from "@/modules/Workspace/WorkspaceModule";

export function meta() {
  return [
    { title: "Workspace | MuniClaw Console" },
    { name: "description", content: "Dedicated Minicloud coding VPS" },
  ];
}

export default function WorkspaceRoute() {
  return <WorkspaceModule />;
}
