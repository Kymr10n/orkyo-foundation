import { Button } from "@foundation/src/components/ui/button";
import { useAppStore } from "@foundation/src/store/app-store";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import {
  ROUTE_CONFIGURATION,
  ROUTE_SETTINGS,
  ROUTE_TENANT_ADMIN,
} from "@foundation/src/constants/auth";
import { cn } from "@foundation/src/lib/utils";
import {
  Box,
  ChevronLeft,
  ChevronRight,
  LayoutDashboard,
  LineChart,
  Building2,
  Package,
  Settings,
  Boxes,
  ShieldCheck,
  Users,
} from "lucide-react";
import { Link, useLocation } from "react-router";

// Split so the per-type entries derived below can sit with the other resource entries,
// beneath the class entries, rather than trailing the whole core list.
// Two classes, never one entry per resource type: adding a type must not grow the sidebar.
const resourceNavItems = [
  { to: "/", label: "Utilization", icon: LayoutDashboard },
  { to: "/stations", label: "Stations", icon: Box },
  { to: "/assets", label: "Assets", icon: Users },
];

const workNavItems = [
  { to: "/requests", label: "Requests", icon: Package },
  { to: "/insights", label: "Insights", icon: LineChart },
  // Organization master data — departments, job titles and whatever else the tenant models about
  // its own structure. Editors maintain the values; the shapes are administration.
  { to: "/organization", label: "Organization", icon: Building2 },
];

// Settings visible to editors and admins; Administration to tenant admins only.
const settingsNavItem = { to: ROUTE_SETTINGS, label: "Settings", icon: Settings };
// Sits next to Administration and shares its gate: both are tenant-admin surfaces, but this one
// shapes the data (resource types, list definitions) rather than governing the tenant. Labelled
// "Configuration", not "Resources" — Stations and Assets are the resources now, and two entries
// claiming the word would send a reader to the wrong one.
const configurationNavItem = { to: ROUTE_CONFIGURATION, label: "Configuration", icon: Boxes };
const adminNavItem = { to: ROUTE_TENANT_ADMIN, label: "Administration", icon: ShieldCheck };

interface SidebarNavProps {
  /**
   * Force a collapsed (icon-rail) or expanded presentation and hide the collapse
   * toggle. The tablet rail passes `true`, the phone drawer passes `false`. When
   * omitted, the sidebar is store-driven with a working toggle (desktop).
   */
  forceCollapsed?: boolean;
  /** Called when a nav link is activated — lets the phone drawer close itself. */
  onNavigate?: () => void;
}

export function SidebarNav({ forceCollapsed, onNavigate }: SidebarNavProps = {}) {
  const location = useLocation();
  const { membership } = useAuth();
  const canEdit = useCanEdit();
  const isTenantAdmin = membership?.isTenantAdmin === true;
  // No per-type entries any more. A type is reached through its class page's selector, which is
  // what keeps the sidebar the same size for a tenant with three types and one with three hundred.
  const navItems = [
    ...resourceNavItems,
    ...workNavItems,
    ...(canEdit ? [settingsNavItem] : []),
    ...(isTenantAdmin ? [adminNavItem, configurationNavItem] : []),
  ];
  const isSidebarCollapsed = useAppStore((state) => state.isSidebarCollapsed);
  const setIsSidebarCollapsed = useAppStore((state) => state.setIsSidebarCollapsed);

  // A forced presentation (tablet rail / phone drawer) ignores the persisted
  // desktop preference and never mutates it.
  const isForced = forceCollapsed !== undefined;
  const collapsed = isForced ? forceCollapsed : isSidebarCollapsed;

  return (
    <nav
      className={cn(
        "border-r bg-card h-full flex flex-col transition-all duration-200",
        collapsed ? "w-16" : "w-60",
      )}
    >
      <div className="flex-1 py-4">
        {navItems.map((item) => {
          const Icon = item.icon;
          // Sections with index-redirect sub-tabs (e.g. Floorplan → /stations/mill/instances)
          // need a prefix match so the parent item stays highlighted. The root
          // item ('/') is special-cased to an exact match so it is not active
          // on every route.
          const isActive =
            item.to === "/"
              ? location.pathname === "/"
              : location.pathname === item.to ||
                location.pathname.startsWith(item.to + "/");

          return (
            <Link
              key={item.to}
              to={item.to}
              onClick={onNavigate}
              className={cn(
                "flex items-center text-sm transition-colors",
                collapsed
                  ? "justify-center px-4 py-2.5"
                  : "gap-3 px-4 py-2.5",
                isActive
                  ? "bg-accent text-accent-foreground font-medium"
                  : "text-muted-foreground hover:text-foreground hover:bg-accent/50",
              )}
              title={collapsed ? item.label : undefined}
            >
              <Icon className="h-4 w-4 flex-shrink-0" />
              {!collapsed && <span>{item.label}</span>}
            </Link>
          );
        })}
      </div>

      {/* Toggle Button — desktop only; the rail and drawer are fixed presentations. */}
      {!isForced && (
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
      )}
    </nav>
  );
}
