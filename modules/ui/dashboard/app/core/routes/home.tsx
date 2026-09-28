import { redirect } from "react-router";

export function clientLoader() {
  return redirect("/tasks");
}

export default function HomeRoute() {
  return null;
}
