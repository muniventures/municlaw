import * as React from "react";
import {
  isRouteErrorResponse,
  Links,
  Meta,
  Outlet,
  Scripts,
  ScrollRestoration,
} from "react-router";
import "./app.css";

export function meta() {
  return [
    { title: "MuniClaw Console" },
    { name: "description", content: "Autonomous coding task management console" },
  ];
}

export function Layout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en">
      <head>
        <meta charSet="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <Meta />
        <Links />
      </head>
      <body className="min-h-screen bg-background text-foreground antialiased font-sans">
        {children}
        <ScrollRestoration />
        <Scripts />
      </body>
    </html>
  );
}

export default function App() {
  return <Outlet />;
}

export function ErrorBoundary({ error }: { error: unknown }) {
  let message = "An unexpected error occurred.";
  let details = "";

  if (isRouteErrorResponse(error)) {
    message = `${error.status} ${error.statusText}`;
    details = error.data?.message || "";
  } else if (error instanceof Error) {
    message = error.message;
    details = error.stack || "";
  }

  return (
    <div className="flex min-h-screen items-center justify-center p-6 bg-background text-foreground">
      <div className="max-w-md w-full space-y-4 rounded-xl border border-destructive/30 bg-destructive/5 p-6 text-center">
        <h1 className="text-xl font-bold text-destructive">Application Error</h1>
        <p className="text-sm text-foreground">{message}</p>
        {details && (
          <pre className="text-[11px] font-mono bg-background p-3 rounded border border-border text-left overflow-x-auto max-h-40">
            {details}
          </pre>
        )}
        <button
          onClick={() => window.location.reload()}
          className="px-4 py-2 text-xs font-semibold rounded-md bg-primary text-primary-foreground hover:bg-primary/90 transition-colors"
        >
          Reload Page
        </button>
      </div>
    </div>
  );
}
