import { Button } from "@/components/ui/button";
import { useAppStore } from "@/store/app-store";
import { cn } from "@/lib/utils";
import {
  AlertTriangle,
  Box,
  ChevronLeft,
  ChevronRight,
  LayoutDashboard,
  Package,
  Settings,
} from "lucide-react";
import { Link, useLocation } from "react-router-dom";

const navItems = [
  { to: "/", label: "Utilization", icon: LayoutDashboard },
  { to: "/spaces", label: "Spaces", icon: Box },
  { to: "/requests", label: "Requests", icon: Package },
  { to: "/conflicts", label: "Conflicts", icon: AlertTriangle },
  { to: "/settings", label: "Settings", icon: Settings },
];

export function SidebarNav() {
  const location = useLocation();
  const isSidebarCollapsed = useAppStore((state) => state.isSidebarCollapsed);
  const setIsSidebarCollapsed = useAppStore((state) => state.setIsSidebarCollapsed);

  return (
    <nav
      className={cn(
        "border-r bg-card h-full flex flex-col transition-all duration-200",
        isSidebarCollapsed ? "w-16" : "w-60",
      )}
    >
      <div className="flex-1 py-4">
        {navItems.map((item) => {
          const Icon = item.icon;
          const isActive = location.pathname === item.to;

          return (
            <Link
              key={item.to}
              to={item.to}
              className={cn(
                "flex items-center text-sm transition-colors",
                isSidebarCollapsed
                  ? "justify-center px-4 py-2.5"
                  : "gap-3 px-4 py-2.5",
                isActive
                  ? "bg-accent text-accent-foreground font-medium"
                  : "text-muted-foreground hover:text-foreground hover:bg-accent/50",
              )}
              title={isSidebarCollapsed ? item.label : undefined}
            >
              <Icon className="h-4 w-4 flex-shrink-0" />
              {!isSidebarCollapsed && <span>{item.label}</span>}
            </Link>
          );
        })}
      </div>

      {/* Toggle Button */}
      <div className="p-2 border-t">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => setIsSidebarCollapsed(!isSidebarCollapsed)}
          className={cn(
            "w-full",
            isSidebarCollapsed ? "px-0 justify-center" : "justify-start",
          )}
          title={isSidebarCollapsed ? "Expand sidebar" : "Collapse sidebar"}
        >
          {isSidebarCollapsed ? (
            <ChevronRight className="h-4 w-4" />
          ) : (
            <>
              <ChevronLeft className="h-4 w-4" />
              <span className="ml-2">Collapse</span>
            </>
          )}
        </Button>
      </div>
    </nav>
  );
}
