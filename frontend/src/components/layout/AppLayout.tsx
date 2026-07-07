import { getSites } from "@foundation/src/lib/api/site-api";
import { useAppStore } from "@foundation/src/store/app-store";
import { useEffect, useRef, useState } from "react";
import { Outlet } from "react-router-dom";
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

export function AppLayout() {
  const selectedSiteId = useAppStore((state) => state.selectedSiteId);
  const setSelectedSiteId = useAppStore((state) => state.setSelectedSiteId);
  const [isSiteValidated, setIsSiteValidated] = useState(false);
  const { isOpen: isCommandPaletteOpen, setIsOpen: setCommandPaletteOpen, open: openCommandPalette } = useCommandPalette();

  const { appUser } = useAuth();
  const [tourOpen, setTourOpen] = useState(false);
  const hasAutoShownTour = useRef(false);

  // Responsive shell: phone gets a drawer behind a hamburger; tablet an icon
  // rail; desktop the full inline sidebar (unchanged).
  const { isPhone, isTablet } = useBreakpoint();
  const [isMobileNavOpen, setIsMobileNavOpen] = useState(false);

  // Leaving the phone layout (resize / rotate) must not strand an open drawer.
  useEffect(() => {
    if (!isPhone && isMobileNavOpen) setIsMobileNavOpen(false);
  }, [isPhone, isMobileNavOpen]);

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
  const lastCommandPaletteTick = useRef(commandPaletteTick);
  const lastTourTick = useRef(tourTick);

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

  // Load sites and validate/set default selection
  useEffect(() => {
    getSites()
      .then((sites) => {
        if (sites.length === 0) {
          setIsSiteValidated(true);
          return;
        }

        // If no site selected or selected site doesn't exist, use first site
        if (!selectedSiteId || !sites.find((s) => s.id === selectedSiteId)) {
          setSelectedSiteId(sites[0].id);
        }
        setIsSiteValidated(true);
      })
      .catch((err: unknown) => {
        logger.error("Failed to load sites:", err);
        setIsSiteValidated(true);
      });
  }, [selectedSiteId, setSelectedSiteId]);

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
      <TopBar onOpenMobileNav={isPhone ? () => setIsMobileNavOpen(true) : undefined} />
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
        <main className="flex-1 overflow-auto p-4">
          <RouteErrorBoundary label="page">
            <Outlet />
          </RouteErrorBoundary>
        </main>
      </div>
      <FeedbackButton />
      <CommandPalette open={isCommandPaletteOpen} onOpenChange={setCommandPaletteOpen} />
      <TourDialog open={tourOpen} onClose={() => setTourOpen(false)} />
    </div>
  );
}
