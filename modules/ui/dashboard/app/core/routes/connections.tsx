import { ConnectionsModule } from "@/modules/Connections/ConnectionsModule";

export function meta() {
  return [
    { title: "Connections | MuniClaw Console" },
    { name: "description", content: "Git connections and BYOK credentials" },
  ];
}

export default function ConnectionsRoute() {
  return <ConnectionsModule />;
}
