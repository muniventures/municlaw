import { OnboardingModule } from "@/modules/Onboarding/OnboardingModule";

export function meta() {
  return [
    { title: "Onboarding | MuniClaw Console" },
    { name: "description", content: "MuniClaw private preview onboarding" },
  ];
}

export default function OnboardingRoute() {
  return <OnboardingModule />;
}
