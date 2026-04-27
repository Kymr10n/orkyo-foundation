import { Button } from "@foundation/src/components/ui/button";
import {
    Popover,
    PopoverContent,
    PopoverTrigger,
} from "@foundation/src/components/ui/popover";
import { Separator } from "@foundation/src/components/ui/separator";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { getSites } from "@foundation/src/lib/api/site-api";
import { getUnreadAnnouncementCount } from "@foundation/src/lib/api/user-announcements-api";
import { useAppStore } from "@foundation/src/store/app-store";
import { navigateToApex } from "@foundation/src/lib/utils/tenant-navigation";
import { ThemeToggle } from "@foundation/src/components/layout/ThemeToggle";
import { useQuery } from "@tanstack/react-query";
import { format } from "date-fns";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import {
    ArrowLeftRight,
    Building,
    Building2,
    Calendar,
    Compass,
    Download,
    Info,
    LogOut,
    Megaphone,
    Search,
    Shield,
    Upload,
    User,
    UserCog,
} from "lucide-react";
import { useNavigate, useLocation } from "react-router-dom";
import { lazy, Suspense, useState } from "react";
import type { ExportContext, ExportFormat, ImportFormat } from "@foundation/src/lib/utils/import-export";
import { isImportSupported } from "@foundation/src/lib/utils/import-export";
import { logger } from "@foundation/src/lib/core/logger";

// Lazy load the import/export dialog to reduce initial bundle size
const ImportExportDialog = lazy(() =>
  import("@foundation/src/components/system/ImportExportDialog").then((m) => ({
    default: m.ImportExportDialog,
  }))
);

export function TopBar() {
  const navigate = useNavigate();
  const location = useLocation();
  const { logout, membership, switchTenant, appUser, sessionData, canAccessAdminPage } = useAuth();

  const _scale = useAppStore((state) => state.scale);
  const anchorTs = useAppStore((state) => state.anchorTs);
  const user = useAppStore((state) => state.user);
  const _setUser = useAppStore((state) => state.setUser);
  const selectedSiteId = useAppStore((state) => state.selectedSiteId);
  const setSelectedSiteId = useAppStore((state) => state.setSelectedSiteId);

  // Dialog state
  const [importDialogOpen, setImportDialogOpen] = useState(false);
  const [exportDialogOpen, setExportDialogOpen] = useState(false);

  // Load sites with React Query
  const { data: sites = [], isLoading: isLoadingSites } = useQuery({
    queryKey: ["sites"],
    queryFn: getSites,
  });

  // Poll unread message count
  const { data: unreadData } = useQuery({
    queryKey: ["unread-announcements"],
    queryFn: getUnreadAnnouncementCount,
    refetchInterval: 60_000, // poll every 60s
    staleTime: 30_000,
  });
  const unreadCount = unreadData?.unreadCount ?? 0;

  // Determine current context based on route
  const getCurrentContext = (): ExportContext | null => {
    const path = location.pathname;
    if (path === '/' || path === '/utilization') return 'utilization';
    if (path === '/spaces') return 'spaces';
    if (path === '/requests') return 'requests';
    if (path === '/conflicts') return 'conflicts';
    if (path.startsWith('/settings')) {
      // For settings, we'll handle this specially based on active tab
      return null; // Will be handled by the settings page itself
    }
    return null;
  };

  const currentContext = getCurrentContext();
  const canImport = currentContext ? isImportSupported(currentContext) : false;
  const canExport = currentContext !== null;

  const handleExport = (format: ExportFormat) => {
    // This will be handled by the dialog component which will call the appropriate handler
    // For now, we'll emit a custom event that pages can listen to
    window.dispatchEvent(new CustomEvent('export-data', {
      detail: { context: currentContext, format }
    }));
  };

  const handleImport = (file: File, format: ImportFormat) => {
    // Emit custom event for pages to handle
    window.dispatchEvent(new CustomEvent('import-data', {
      detail: { context: currentContext, format, file }
    }));
  };

  const handleLogout = async () => {
    try {
      logout();
    } catch (error) {
      logger.error('Logout failed:', error);
    }
  };

  const isMultiTenant = (sessionData?.tenants.length ?? 0) > 1;

  const handleSwitchTenant = () => {
    // Production: full-page redirect to apex, machine re-bootstraps and shows selector.
    // Local dev: navigateToApex returns false — drive the machine directly.
    if (!navigateToApex('/')) {
      switchTenant();
    }
  };

  return (
    <header className="h-14 border-b bg-card flex items-center px-4 gap-4 sticky top-0 z-50">
      <div className="font-semibold text-base whitespace-nowrap">
        Orkyo
      </div>

      {/* Current Organisation */}
      {membership && (
        <div className="flex items-center gap-2 text-muted-foreground">
          <span className="text-sm">/</span>
          <Building className="h-4 w-4" />
          <span className="text-sm font-medium">{membership.displayName}</span>
        </div>
      )}

      {/* Scale & Anchor Date */}
      <div className="flex items-center gap-2">
        <Calendar className="h-4 w-4 text-muted-foreground" />
        <span className="text-sm text-muted-foreground whitespace-nowrap">
          {format(anchorTs, "MMM d, yyyy")}
        </span>
      </div>

      {/* Site Selector — visible when tenant has multiple sites */}
      {sites.length > 1 && (
        <div className="flex items-center gap-2">
          <Building2 className="h-4 w-4 text-muted-foreground" />
          <Select
            value={selectedSiteId ?? ''}
            onValueChange={setSelectedSiteId}
            disabled={isLoadingSites || sites.length === 0}
          >
            <SelectTrigger className="w-[180px]">
              <SelectValue placeholder="Select site..." />
            </SelectTrigger>
            <SelectContent>
              {sites.map((site) => (
                <SelectItem key={site.id} value={site.id}>
                  {site.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      )}

      {/* Time Cursor removed - now in UtilizationPage header */}

      <div className="flex-1" />

      <div className="flex items-center gap-2">
        {/* Search button - opens command palette */}
        <Button
          variant="ghost"
          size="icon"
          onClick={() => window.dispatchEvent(new CustomEvent('open-command-palette'))}
          title="Search (⌘K)"
        >
          <Search className="h-4 w-4" />
        </Button>

        {/* Contextual Import button */}
        <Button
          variant="ghost"
          size="icon"
          onClick={() => setImportDialogOpen(true)}
          disabled={!canImport}
          title={canImport ? `Import ${currentContext}` : 'Import not available'}
        >
          <Upload className="h-4 w-4" />
        </Button>

        {/* Contextual Export button */}
        <Button
          variant="ghost"
          size="icon"
          onClick={() => setExportDialogOpen(true)}
          disabled={!canExport}
          title={canExport ? `Export ${currentContext}` : 'Export not available'}
        >
          <Download className="h-4 w-4" />
        </Button>

        <ThemeToggle />

        {/* Unread messages indicator */}
        {unreadCount > 0 && (
          <Button
            variant="ghost"
            size="icon"
            className="relative"
            onClick={() => navigate("/messages")}
            title={`${unreadCount} unread message${unreadCount !== 1 ? 's' : ''}`}
          >
            <Megaphone className="h-4 w-4" />
            <span className="absolute -top-0.5 -right-0.5 flex h-4 min-w-4 items-center justify-center rounded-full bg-primary px-1 text-[10px] font-bold text-primary-foreground">
              {unreadCount > 9 ? '9+' : unreadCount}
            </span>
          </Button>
        )}

        <Popover>
          <PopoverTrigger asChild>
            <Button variant="ghost" size="icon" data-testid="user-menu-trigger">
              <User className="h-4 w-4" />
            </Button>
          </PopoverTrigger>
          <PopoverContent align="end" className="w-64">
            <div className="space-y-3">
              <div className="space-y-1">
                <p className="text-sm font-medium">
                  {user?.displayName || appUser?.displayName || "User"}
                </p>
                <p className="text-xs text-muted-foreground">{user?.email || appUser?.email}</p>
              </div>

              <Separator />

              <div className="space-y-1">
                <Button
                  variant="ghost"
                  className="w-full justify-start h-9"
                  onClick={() => navigate("/account")}
                >
                  <UserCog className="h-4 w-4 mr-2" />
                  Account
                </Button>

                {isMultiTenant && !membership?.isBreakGlass && (
                  <Button
                    variant="ghost"
                    className="w-full justify-start h-9"
                    onClick={handleSwitchTenant}
                    data-testid="switch-organization-btn"
                  >
                    <ArrowLeftRight className="h-4 w-4 mr-2" />
                    Switch Organization
                  </Button>
                )}

                {canAccessAdminPage && !membership?.isBreakGlass && (
                  <Button
                    variant="ghost"
                    className="w-full justify-start h-9"
                    data-testid="admin-panel-btn"
                    onClick={() => {
                      if (!navigateToApex('/admin')) navigate('/admin');
                    }}
                  >
                    <Shield className="h-4 w-4 mr-2" />
                    Admin Panel
                  </Button>
                )}

                <Button
                  variant="ghost"
                  className="w-full justify-start h-9"
                  onClick={() => navigate("/messages")}
                >
                  <Megaphone className="h-4 w-4 mr-2" />
                  Messages
                </Button>

                <Button
                  variant="ghost"
                  className="w-full justify-start h-9"
                  onClick={() => window.dispatchEvent(new CustomEvent('open-tour'))}
                >
                  <Compass className="h-4 w-4 mr-2" />
                  Take a tour
                </Button>

                <Button
                  variant="ghost"
                  className="w-full justify-start h-9"
                  onClick={() => navigate("/about")}
                >
                  <Info className="h-4 w-4 mr-2" />
                  About
                </Button>

                <Button
                  variant="ghost"
                  className="w-full justify-start h-9 text-destructive hover:text-destructive"
                  onClick={handleLogout}
                >
                  <LogOut className="h-4 w-4 mr-2" />
                  Sign out
                </Button>
              </div>
            </div>
          </PopoverContent>
        </Popover>
      </div>

      {/* Import/Export Dialogs */}
      {currentContext && (
        <Suspense fallback={null}>
          <ImportExportDialog
            open={importDialogOpen}
            onOpenChange={setImportDialogOpen}
            mode="import"
            context={currentContext}
            onImport={handleImport}
            siteId={selectedSiteId || undefined}
          />
          <ImportExportDialog
            open={exportDialogOpen}
            onOpenChange={setExportDialogOpen}
            mode="export"
            context={currentContext}
            onExport={handleExport}
            siteId={selectedSiteId || undefined}
          />
        </Suspense>
      )}
    </header>
  );
}
