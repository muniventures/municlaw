import React from "react";
import { Link, useLocation } from "react-router";
import {
  ListTodo,
  FolderGit2,
  KeyRound,
  Server,
  Settings,
  ShieldCheck,
  Terminal,
} from "lucide-react";
import { cn } from "@/components/ui/utils";

interface NavItem {
  name: string;
  href: string;
  icon: React.ComponentType<{ className?: string }>;
  badge?: string;
}

const NAV_ITEMS: NavItem[] = [
  { name: "Tasks", href: "/tasks", icon: ListTodo },
  { name: "Projects", href: "/projects", icon: FolderGit2 },
  { name: "Connections", href: "/connections", icon: KeyRound },
  { name: "Workspace", href: "/workspace", icon: Server },
  { name: "Settings", href: "/settings", icon: Settings },
];

interface SidebarProps {
  onNavClick?: () => void;
  className?: string;
}

export function Sidebar({ onNavClick, className }: SidebarProps) {
  const location = useLocation();

  return (
    <aside
      className={cn(
        "flex flex-col h-full bg-card border-r border-border text-card-foreground select-none",
        className
      )}
      aria-label="Main Navigation"
    >
      {/* Brand Header */}
      <div className="h-14 flex items-center px-4 border-b border-border gap-2.5">
        <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-primary text-primary-foreground font-bold shadow-xs">
          <Terminal className="h-4 w-4" />
        </div>
        <div className="flex flex-col">
          <span className="font-semibold text-sm tracking-tight leading-none flex items-center gap-1.5">
            MuniClaw
            <span className="text-[10px] uppercase font-mono px-1.5 py-0.5 rounded bg-muted text-muted-foreground font-medium">
              Console
            </span>
          </span>
          <span className="text-[11px] text-muted-foreground font-mono mt-0.5">
            ai.muni.dev
          </span>
        </div>
      </div>

      {/* Nav List */}
      <nav className="flex-1 px-3 py-4 space-y-1 overflow-y-auto" role="navigation">
        <div className="px-2 pb-2 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
          Platform
        </div>
        {NAV_ITEMS.map((item) => {
          const isActive =
            item.href === "/tasks"
              ? location.pathname === "/" || location.pathname.startsWith("/tasks")
              : location.pathname.startsWith(item.href);
          const Icon = item.icon;

          return (
            <Link
              key={item.href}
              to={item.href}
              onClick={onNavClick}
              aria-current={isActive ? "page" : undefined}
              className={cn(
                "group flex items-center gap-3 px-3 py-2 rounded-md text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
                isActive
                  ? "bg-primary text-primary-foreground shadow-xs font-semibold"
                  : "text-muted-foreground hover:bg-muted hover:text-foreground"
              )}
            >
              <Icon
                className={cn(
                  "h-4 w-4 shrink-0 transition-colors",
                  isActive
                    ? "text-primary-foreground"
                    : "text-muted-foreground group-hover:text-foreground"
                )}
              />
              <span className="truncate">{item.name}</span>
              {item.badge && (
                <span
                  className={cn(
                    "ml-auto text-xs px-1.5 py-0.5 rounded-full font-mono font-medium",
                    isActive
                      ? "bg-primary-foreground/20 text-primary-foreground"
                      : "bg-muted text-muted-foreground"
                  )}
                >
                  {item.badge}
                </span>
              )}
            </Link>
          );
        })}
      </nav>

      {/* Security & Private VPS Banner */}
      <div className="p-3 m-3 rounded-lg border border-border bg-muted/40 text-xs">
        <div className="flex items-center gap-2 font-medium text-foreground mb-1">
          <ShieldCheck className="h-3.5 w-3.5 text-emerald-600" />
          <span>Private Sandboxing</span>
        </div>
        <p className="text-[11px] text-muted-foreground leading-normal">
          Isolated Minicloud VPS per org. Provider keys write-only.
        </p>
      </div>
    </aside>
  );
}
