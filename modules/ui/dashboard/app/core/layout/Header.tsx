import { Menu, Building2, CheckCircle2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";

interface HeaderProps {
  onToggleMobileNav: () => void;
  orgName?: string;
  userEmail?: string;
  isAllowlisted?: boolean;
}

export function Header({
  onToggleMobileNav,
  orgName = "Acme Engineering",
  userEmail = "dev@acme.corp",
  isAllowlisted = true,
}: HeaderProps) {
  return (
    <header className="h-14 border-b border-border bg-card/80 backdrop-blur-xs px-4 flex items-center justify-between z-10 sticky top-0">
      <div className="flex items-center gap-3">
        {/* Mobile menu trigger */}
        <Button
          variant="ghost"
          size="icon"
          className="md:hidden"
          onClick={onToggleMobileNav}
          aria-label="Toggle mobile menu"
        >
          <Menu className="h-5 w-5" />
        </Button>

        {/* Organization Info */}
        <div className="flex items-center gap-2">
          <div className="flex items-center gap-1.5 px-2 py-1 rounded-md bg-muted text-xs font-medium text-foreground">
            <Building2 className="h-3.5 w-3.5 text-muted-foreground" />
            <span className="font-semibold">{orgName}</span>
          </div>

          {isAllowlisted ? (
            <Badge variant="success" className="hidden sm:inline-flex gap-1 text-[11px]">
              <CheckCircle2 className="h-3 w-3" />
              Allowlisted
            </Badge>
          ) : (
            <Badge variant="destructive" className="gap-1 text-[11px]">
              Allowlist Required
            </Badge>
          )}
        </div>
      </div>

      {/* User Status */}
      <div className="flex items-center gap-3 text-xs">
        <span className="hidden sm:inline text-muted-foreground font-mono">
          {userEmail}
        </span>
        <div className="h-7 w-7 rounded-full bg-primary/10 border border-primary/20 flex items-center justify-center font-semibold text-primary text-xs">
          {userEmail.charAt(0).toUpperCase()}
        </div>
      </div>
    </header>
  );
}
