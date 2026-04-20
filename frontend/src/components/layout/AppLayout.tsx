import { getSites } from "@/lib/api/site-api";
import { useAppStore } from "@/store/app-store";
import { useEffect, useRef, useState } from "react";
import { Outlet } from "react-router-dom";
import { CommandPalette } from "./CommandPalette";
import { FeedbackButton } from "./FeedbackButton";
import { SidebarNav } from "./SidebarNav";
import { TopBar } from "./TopBar";
import { useCommandPalette } from "@/hooks/useCommandPalette";
import { useAuth } from "@/contexts/AuthContext";
import { TourDialog } from "@/components/tour/TourDialog";
import { logger } from "@/lib/core/logger";

export function AppLayout() {
  const selectedSiteId = useAppStore((state) => state.selectedSiteId);
  const setSelectedSiteId = useAppStore((state) => state.setSelectedSiteId);
  const [isSiteValidated, setIsSiteValidated] = useState(false);
  const { isOpen: isCommandPaletteOpen, setIsOpen: setCommandPaletteOpen, open: openCommandPalette } = useCommandPalette();

  const { appUser } = useAuth();
  const [tourOpen, setTourOpen] = useState(false);
  const hasAutoShownTour = useRef(false);

  // Auto-show tour once per session for users who haven't seen it
  useEffect(() => {
    if (!hasAutoShownTour.current && appUser && !appUser.hasSeenTour) {
      hasAutoShownTour.current = true;
      setTourOpen(true);
    }
  }, [appUser]);

  // Listen for open-command-palette events from TopBar
  useEffect(() => {
    const handleOpenCommandPalette = () => openCommandPalette();
    window.addEventListener('open-command-palette', handleOpenCommandPalette);
    return () => window.removeEventListener('open-command-palette', handleOpenCommandPalette);
  }, [openCommandPalette]);

  // Listen for open-tour events from TopBar user menu
  useEffect(() => {
    const handleOpenTour = () => setTourOpen(true);
    window.addEventListener('open-tour', handleOpenTour);
    return () => window.removeEventListener('open-tour', handleOpenTour);
  }, []);

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
        <div className="flex-1 flex items-center justify-center">
          <div className="text-muted-foreground">Loading...</div>
        </div>
      </div>
    );
  }

  return (
    <div className="h-screen flex flex-col">
      <TopBar />
      <div className="flex-1 flex overflow-hidden">
        <SidebarNav />
        <main className="flex-1 overflow-auto p-4">
          <Outlet />
        </main>
      </div>
      <FeedbackButton />
      <CommandPalette open={isCommandPaletteOpen} onOpenChange={setCommandPaletteOpen} />
      <TourDialog open={tourOpen} onClose={() => setTourOpen(false)} />
    </div>
  );
}
