import { STORAGE_KEYS } from "@foundation/src/constants/storage";
import { applyResolvedTheme, resolveTheme, type ThemePreference } from "@foundation/src/lib/core/theme";
import { create } from "zustand";
import { persist } from "zustand/middleware";

/**
 * How the shell is arranged for this user: what is collapsed, and which theme.
 *
 * These are per-machine preferences, so the whole store persists through zustand
 * `persist` under one key. `resolvedTheme` is derived from `theme` plus the OS
 * setting, so it is recomputed on every load rather than stored.
 *
 * `collapsedGroupIds` holds resource-group ids the user folded away on the
 * scheduler. Ids that no longer exist are inert — the grid only asks whether a
 * group it is rendering is in the list — so the list needs no reconciliation.
 */
interface LayoutState {
  isSidebarCollapsed: boolean;
  setIsSidebarCollapsed: (collapsed: boolean) => void;

  isFloorplanCollapsed: boolean;
  setIsFloorplanCollapsed: (collapsed: boolean) => void;

  collapsedGroupIds: string[];
  toggleGroupCollapse: (groupId: string) => void;

  theme: ThemePreference;
  /** "system" resolved against the OS setting. Derived, never persisted. */
  resolvedTheme: "dark" | "light";
  setTheme: (theme: ThemePreference) => void;
}

export const useLayoutStore = create<LayoutState>()(
  persist(
    (set) => ({
      isSidebarCollapsed: false,
      setIsSidebarCollapsed: (isSidebarCollapsed) => set({ isSidebarCollapsed }),

      isFloorplanCollapsed: false,
      setIsFloorplanCollapsed: (isFloorplanCollapsed) => set({ isFloorplanCollapsed }),

      collapsedGroupIds: [],
      toggleGroupCollapse: (groupId) =>
        set((state) => ({
          collapsedGroupIds: state.collapsedGroupIds.includes(groupId)
            ? state.collapsedGroupIds.filter((id) => id !== groupId)
            : [...state.collapsedGroupIds, groupId],
        })),

      theme: "system",
      resolvedTheme: resolveTheme("system"),
      setTheme: (theme) => {
        const resolvedTheme = resolveTheme(theme);
        applyResolvedTheme(resolvedTheme);
        set({ theme, resolvedTheme });
      },
    }),
    {
      name: STORAGE_KEYS.LAYOUT,
      // resolvedTheme is derived; persisting it would pin a stale OS answer.
      partialize: ({ isSidebarCollapsed, isFloorplanCollapsed, collapsedGroupIds, theme }) => ({
        isSidebarCollapsed,
        isFloorplanCollapsed,
        collapsedGroupIds,
        theme,
      }),
      onRehydrateStorage: () => (state) => {
        if (state) state.resolvedTheme = resolveTheme(state.theme);
      },
    },
  ),
);

// Follow the OS while the preference is "system". The listener is registered once, at
// module load, because the theme applies to the whole document and not to a component.
if (typeof window !== "undefined" && window.matchMedia) {
  window
    .matchMedia("(prefers-color-scheme: dark)")
    .addEventListener("change", (e) => {
      if (useLayoutStore.getState().theme !== "system") return;
      const resolvedTheme = e.matches ? "dark" : "light";
      applyResolvedTheme(resolvedTheme);
      useLayoutStore.setState({ resolvedTheme });
    });
}
