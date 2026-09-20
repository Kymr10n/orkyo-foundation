import { STORAGE_KEYS } from "@foundation/src/constants/storage";
import { create } from "zustand";
import { persist } from "zustand/middleware";

/**
 * The site the user is looking at, or null for "all sites".
 *
 * One concern, one store. Read it with a selector (`useSiteStore((s) => s.selectedSiteId)`)
 * so a component that only needs the site does not re-render when an unrelated
 * preference changes — which is what the single app-wide store used to cost.
 *
 * Persisted, because the picker is a session-spanning choice: coming back to the app
 * on the same machine and landing on a different site reads as a bug. `null` persists
 * too, so "all sites" is a choice the reload keeps rather than silently forgetting.
 */
interface SiteState {
  selectedSiteId: string | null;
  setSelectedSiteId: (siteId: string | null) => void;
}

export const useSiteStore = create<SiteState>()(
  persist(
    (set) => ({
      selectedSiteId: null,
      setSelectedSiteId: (selectedSiteId) => set({ selectedSiteId }),
    }),
    { name: STORAGE_KEYS.SELECTED_SITE_ID },
  ),
);
