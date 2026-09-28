import { SettingsModule } from "@/modules/Settings/SettingsModule";

export function meta() {
  return [
    { title: "Settings | MuniClaw Console" },
    { name: "description", content: "Organization settings and capability approval policies" },
  ];
}

export default function SettingsRoute() {
  return <SettingsModule />;
}
