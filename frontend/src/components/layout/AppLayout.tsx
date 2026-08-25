import { useSites } from "@foundation/src/hooks/useSites";
import { useAppStore } from "@foundation/src/store/app-store";
import { useCallback, useEffect, useRef, useState } from "react";
import { Outlet, useNavigate } from "react-router";
import { CommandPalette } from "./CommandPalette";
import { FeedbackButton } from "./FeedbackButton";
import { SidebarNav } from "./SidebarNav";
import { TopBar } from "./TopBar";
import { LoadingSpinner } from "@foundation/src/components/ui/LoadingSpinner";
import { RouteErrorBoundary } from "@foundation/src/components/ui/RouteErrorBoundary";
import {
  Sheet,
  SheetContent,
  SheetHeader,
  SheetTitle,
} from "@foundation/src/components/ui/sheet";
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint";
import { useCommandPalette } from "@foundation/src/hooks/useCommandPalette";
import { useAuth } from "@foundation/src/contexts/AuthContext";
import { TourDialog } from "@foundation/src/components/tour/TourDialog";
import { logger } from "@foundation/src/lib/core/logger";
import { useUiActionsStore } from "@foundation/src/store/ui-actions-store";
import { AssistantPanel } from "@foundation/src/components/assistant/AssistantPanel";
import { resolveView } from "@foundation/src/components/assistant/view-catalog";
import { updateRequest } from "@foundation/src/lib/api/request-api";
import type { UpdateRequestRequest } from "@foundation/src/types/requests";
import { qk } from "@foundation/src/lib/api/query-keys";
import { useQueryClient } from "@tanstack/react-query";

interface AppLayoutProps {
  /** Edition-supplied plans-page href for the tier-gated upsells (calendar subscription, data export / import). */
  upgradeHref?: string;
}

export function AppLayout({ upgradeHref }: AppLayoutProps = {}) {
  const selectedSiteId = useAppStore((state) => state.selectedSiteId);
  const setSelectedSiteId = useAppStore((state) => state.setSelectedSiteId);
  const { isOpen: isCommandPaletteOpen, setIsOpen: setCommandPaletteOpen, open: openCommandPalette } = useCommandPalette();

  const { appUser } = useAuth();
  const [tourOpen, setTourOpen] = useState(false);
  const queryClient = useQueryClient();
  const hasAutoShownTour = useRef(false);

  // Responsive shell: phone gets a drawer behind a hamburger; tablet an icon
  // rail; desktop the full inline sidebar (unchanged).
  const { isPhone, isTablet } = useBreakpoint();
  const [isMobileNavOpen, setIsMobileNavOpen] = useState(false);

  // Leaving the phone layout (resize / rotate) must not strand an open drawer. Render-phase
  // update, not an effect (see useEntityFormDialog.ts).
  const [syncedIsPhone, setSyncedIsPhone] = useState(isPhone);
  if (syncedIsPhone !== isPhone) {
    setSyncedIsPhone(isPhone);
    if (!isPhone) setIsMobileNavOpen(false);
  }

  // Auto-show tour once per session for users who haven't seen it
  useEffect(() => {
    if (!hasAutoShownTour.current && appUser && !appUser.hasSeenTour) {
      hasAutoShownTour.current = true;
      setTourOpen(true);
    }
  }, [appUser]);

  // Subscribe to ui-actions triggers from TopBar. Each tick increment means
  // a fresh trigger to consume.
  const commandPaletteTick = useUiActionsStore((s) => s.commandPaletteTick);
  const tourTick = useUiActionsStore((s) => s.tourTick);
  const assistantTick = useUiActionsStore((s) => s.assistantTick);
  const assistantContext = useUiActionsStore((s) => s.assistantContext);
  const requestAutoSchedule = useUiActionsStore((s) => s.requestAutoSchedule);
  const navigate = useNavigate();

  // Resolved against the client's own catalog: the server sends an id, never a path, so a
  // view this app does not know moves nobody. Site is switched first, in the order
  // CommandPalette uses, or the record opens under the wrong site. Memoised because the
  // panel takes it as a dependency of the turn loop.
  const openView = useCallback(
    (view: string, entityId: string | null, siteId: string | null) => {
      const target = resolveView(view, entityId);
      if (!target) return null;
      if (siteId && siteId !== selectedSiteId) setSelectedSiteId(siteId);
      navigate(target.path);
      return target.label;
    },
    [navigate, selectedSiteId, setSelectedSiteId],
  );
  const lastCommandPaletteTick = useRef(commandPaletteTick);
  const lastTourTick = useRef(tourTick);
  const lastAssistantTick = useRef(assistantTick);
  const [assistantOpen, setAssistantOpen] = useState(false);

  useEffect(() => {
    if (commandPaletteTick !== lastCommandPaletteTick.current) {
      lastCommandPaletteTick.current = commandPaletteTick;
      openCommandPalette();
    }
  }, [commandPaletteTick, openCommandPalette]);

  useEffect(() => {
    if (tourTick !== lastTourTick.current) {
      lastTourTick.current = tourTick;
      setTourOpen(true);
    }
  }, [tourTick]);

  useEffect(() => {
    if (assistantTick !== lastAssistantTick.current) {
      lastAssistantTick.current = assistantTick;
      setAssistantOpen(true);
    }
  }, [assistantTick]);

  // Load sites (shared React Query cache) and validate/set default selection.
  const { data: sites, isSuccess: sitesLoaded, isError: sitesError, error: sitesLoadError } = useSites();
  // Derived, not stored: we are validated once sites failed to load, or loaded and either
  // there are none or the current selection actually exists. Deriving keeps the effect below
  // free of setState — it only writes the external store and logs.
  const siteSelectionValid =
    sitesError ||
    (sitesLoaded && !!sites && (sites.length === 0 || sites.some((s) => s.id === selectedSiteId)));

  // Latched, because the gate below unmounts the whole route tree. The first validation is a
  // real gate (it stops pages firing API calls with a stale site id before we know it is good).
  // Afterwards it must stay open: if a refetch later drops the selected site — deleted here or
  // in another session — the effect below swaps the store to a valid one within the same tick,
  // and re-closing the gate for that tick would discard every page's local state (open dialogs,
  // half-typed forms, scroll position) instead of quietly swapping underneath it.
  const [everValidated, setEverValidated] = useState(false);
  if (siteSelectionValid && !everValidated) setEverValidated(true);
  const isSiteValidated = siteSelectionValid || everValidated;

  useEffect(() => {
    if (sitesError) {
      logger.error("Failed to load sites:", sitesLoadError);
      return;
    }
    if (!sitesLoaded || !sites || sites.length === 0) return;

    // If no site selected or selected site doesn't exist, use first site
    if (!selectedSiteId || !sites.find((s) => s.id === selectedSiteId)) {
      setSelectedSiteId(sites[0].id);
    }
  }, [sitesLoaded, sitesError, sitesLoadError, sites, selectedSiteId, setSelectedSiteId]);

  // Don't render child routes until site is validated - prevents stale site ID API calls
  if (!isSiteValidated) {
    return (
      <div className="h-screen flex flex-col">
        <TopBar />
        <div className="flex-1">
          <LoadingSpinner fullScreen={false} message="Loading…" />
        </div>
      </div>
    );
  }

  return (
    <div className="h-screen flex flex-col">
      <TopBar
        onOpenMobileNav={isPhone ? () => setIsMobileNavOpen(true) : undefined}
        upgradeHref={upgradeHref}
      />
      <div className="flex-1 flex overflow-hidden">
        {isPhone ? (
          <Sheet open={isMobileNavOpen} onOpenChange={setIsMobileNavOpen}>
            <SheetContent side="left" className="w-60 p-0 sm:max-w-none">
              <SheetHeader className="sr-only">
                <SheetTitle>Navigation</SheetTitle>
              </SheetHeader>
              <SidebarNav forceCollapsed={false} onNavigate={() => setIsMobileNavOpen(false)} />
            </SheetContent>
          </Sheet>
        ) : (
          <SidebarNav forceCollapsed={isTablet ? true : undefined} />
        )}
        {/* On phones PageLayout is the single padding owner (UI-GUIDELINES §16);
            main pads only from md: up so desktop metrics stay unchanged. */}
        <main className="flex-1 overflow-auto md:p-4">
          <RouteErrorBoundary label="page">
            <Outlet />
          </RouteErrorBoundary>
        </main>
      </div>
      <FeedbackButton />
      <CommandPalette open={isCommandPaletteOpen} onOpenChange={setCommandPaletteOpen} />
      <TourDialog open={tourOpen} onClose={() => setTourOpen(false)} />
      <AssistantPanel
        open={assistantOpen}
        onOpenChange={setAssistantOpen}
        context={assistantContext}
        // Applying goes through the ordinary request endpoint under this person's own
        // session, so the same validation and permissions apply as to a manual edit.
        // Accepting an auto-schedule proposal does not schedule anything: it takes the
        // person to the scheduling page and opens the ordinary preview for exactly the
        // requests they approved, so the solver's plan is reviewed before it is written.
        // Resolved against the client's own catalog: the server sends an id, never a
        // path, so a view this app does not know moves nobody. Site is switched first, in
        // the order CommandPalette uses, or the record opens under the wrong site.
        onOpenView={openView}
        onApplyAutoSchedule={async (requestIds) => {
          navigate("/");
          requestAutoSchedule(requestIds);
        }}
        onApplyProposal={async (requestId, changes) => {
          await updateRequest(requestId, changes as UpdateRequestRequest);
          await queryClient.invalidateQueries({ queryKey: qk.requests.all() });
          await queryClient.invalidateQueries({ queryKey: qk.conflicts.all() });
        }}
      />
    </div>
  );
}
