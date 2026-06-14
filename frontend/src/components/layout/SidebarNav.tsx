import { Button } from "@foundation/src/components/ui/button";
import { useAppStore } from "@foundation/src/store/app-store";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import { cn } from "@foundation/src/lib/utils";
import {
  AlertTriangle,
  Box,
  ChevronLeft,
  ChevronRight,
  LayoutDashboard,
  Package,
  Settings,
  ShieldCheck,
  Users,
} from "lucide-react";
import { Link, useLocation } from "react-router-dom";

const coreNavItems = [
  { to: "/", label: "Utilization", icon: LayoutDashboard },
  { to: "/spaces", label: "Spaces", icon: Box },
  { to: "/people", label: "People", icon: Users },
  { to: "/requests", label: "Requests", icon: Package },
  { to: "/conflicts", label: "Conflicts", icon: AlertTriangle },
];

// Settings visible to editors and admins; Administration to tenant admins only.
const settingsNavItem = { to: "/settings", label: "Settings", icon: Settings };
const adminNavItem = { to: "/tenant-admin", label: "Administration", icon: ShieldCheck };

export function SidebarNav() {
  const location = useLocation();
  const { membership } = useAuth();
  const canEdit = useCanEdit();
  const isTenantAdmin = membership?.isTenantAdmin === true;
  const navItems = [
    ...coreNavItems,
    ...(canEdit ? [settingsNavItem] : []),
    ...(isTenantAdmin ? [adminNavItem] : []),
  ];
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
