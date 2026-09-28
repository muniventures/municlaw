import * as React from "react";
import { Outlet } from "react-router";
import { Sidebar } from "./Sidebar";
import { Header } from "./Header";
import { getRuntimeConfig } from "@/core/config/runtime";

export function AppShell() {
  const [mobileNavOpen, setMobileNavOpen] = React.useState(false);
  const [config] = React.useState(() => getRuntimeConfig());

  // Close mobile nav on Esc key
  React.useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === "Escape" && mobileNavOpen) {
        setMobileNavOpen(false);
      }
    };
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [mobileNavOpen]);

  return (
    <div className="flex h-screen w-screen overflow-hidden bg-background text-foreground">
      {/* Skip to main content link for a11y */}
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:absolute focus:top-2 focus:left-2 focus:z-50 focus:px-4 focus:py-2 focus:bg-primary focus:text-primary-foreground focus:rounded-md focus:shadow-md"
      >
        Skip to main content
      </a>

      {/* Desktop Sidebar */}
      <div className="hidden md:flex md:w-64 md:shrink-0">
        <Sidebar className="w-full" />
      </div>

      {/* Mobile Drawer Backdrop and Sidebar */}
      {mobileNavOpen && (
        <div className="fixed inset-0 z-50 flex md:hidden" role="dialog" aria-modal="true">
          <div
            className="fixed inset-0 bg-black/60 backdrop-blur-xs transition-opacity animate-in fade-in"
            onClick={() => setMobileNavOpen(false)}
            aria-hidden="true"
          />
          <div className="relative flex w-4/5 max-w-xs flex-1 flex-col bg-card animate-in slide-in-from-left duration-200 shadow-2xl">
            <Sidebar onNavClick={() => setMobileNavOpen(false)} className="w-full" />
          </div>
        </div>
      )}

      {/* Main Content Area */}
      <div className="flex flex-1 flex-col min-w-0 overflow-hidden">
        <Header
          onToggleMobileNav={() => setMobileNavOpen((prev) => !prev)}
          orgName="Acme Engineering"
          userEmail="developer@acme.corp"
          isAllowlisted={true}
        />
        <main
          id="main-content"
          tabIndex={-1}
          className="flex-1 overflow-y-auto focus:outline-none p-4 md:p-6 lg:p-8"
        >
          <div className="mx-auto max-w-7xl">
            <Outlet context={{ config }} />
          </div>
        </main>
      </div>
    </div>
  );
}
