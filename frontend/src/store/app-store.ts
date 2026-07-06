import { STORAGE_KEYS } from "@foundation/src/constants/storage";
import { writeThemeCookie } from "@foundation/src/lib/core/theme";
import { create } from "zustand";

interface AppState {
  // Site and hall selection
  selectedSiteId: string | null;
  selectedHallId: string | null;
  setSelectedSiteId: (siteId: string | null) => void;
  setSelectedHallId: (hallId: string | null) => void;

  // Scheduler time controls (v3)
  scale: "year" | "month" | "week" | "day" | "hour";
  anchorTs: Date;
  setScale: (scale: "year" | "month" | "week" | "day" | "hour") => void;
  setAnchorTs: (ts: Date) => void;

  // Time cursor
  timeCursorTs: Date;
  setTimeCursorTs: (ts: Date) => void;

  // Selection state for utilization
  selectedResourceId: string | null;
  selectedJobId: string | null;
  selectedRequestId: string | null;
  setSelectedResourceId: (id: string | null) => void;
  setSelectedJobId: (id: string | null) => void;
  setSelectedRequestId: (id: string | null) => void;

  // Space row ordering
  spaceOrder: string[];
  setSpaceOrder: (order: string[]) => void;

  // Drawer state
  isDetailsDrawerOpen: boolean;
  setIsDetailsDrawerOpen: (open: boolean) => void;

  // Floorplan collapse state (v3)
  isFloorplanCollapsed: boolean;
  setIsFloorplanCollapsed: (collapsed: boolean) => void;

  // Sidebar collapse state
  isSidebarCollapsed: boolean;
  setIsSidebarCollapsed: (collapsed: boolean) => void;

  // Space groups collapse state
  collapsedGroupIds: string[];
  toggleGroupCollapse: (groupId: string) => void;

  // Theme
  theme: "dark" | "light" | "system";
  resolvedTheme: "dark" | "light";
  setTheme: (theme: "dark" | "light" | "system") => void;
}

export const useAppStore = create<AppState>((set) => ({
  selectedSiteId:
    typeof localStorage !== "undefined"
      ? localStorage.getItem(STORAGE_KEYS.SELECTED_SITE_ID)
      : null,
  selectedHallId: null,
  setSelectedSiteId: (siteId) => {
    set({ selectedSiteId: siteId });
    if (typeof localStorage !== "undefined" && siteId) {
      localStorage.setItem(STORAGE_KEYS.SELECTED_SITE_ID, siteId);
    }
  },
  setSelectedHallId: (hallId) => set({ selectedHallId: hallId }),

  scale: "week",
  anchorTs: new Date(),
  setScale: (scale) => set({ scale }),
  setAnchorTs: (ts) => set({ anchorTs: ts }),

  timeCursorTs: new Date(),
  setTimeCursorTs: (ts) => set({ timeCursorTs: ts }),

  selectedResourceId: null,
  selectedJobId: null,
  selectedRequestId: null,
  setSelectedResourceId: (id) =>
    set({ selectedResourceId: id, isDetailsDrawerOpen: !!id }),
  setSelectedJobId: (id) =>
    set({ selectedJobId: id, isDetailsDrawerOpen: !!id }),
  setSelectedRequestId: (id) =>
    set({ selectedRequestId: id, isDetailsDrawerOpen: !!id }),

  spaceOrder: [],
  setSpaceOrder: (order) => set({ spaceOrder: order }),

  isDetailsDrawerOpen: false,
  setIsDetailsDrawerOpen: (open) => set({ isDetailsDrawerOpen: open }),

  isFloorplanCollapsed: false,
  setIsFloorplanCollapsed: (collapsed) => set({ isFloorplanCollapsed: collapsed }),

  isSidebarCollapsed:
    typeof localStorage !== "undefined" &&
    localStorage.getItem(STORAGE_KEYS.SIDEBAR_COLLAPSED) === "true",
  setIsSidebarCollapsed: (collapsed) => {
    if (typeof localStorage !== "undefined") {
      localStorage.setItem(STORAGE_KEYS.SIDEBAR_COLLAPSED, String(collapsed));
    }
    set({ isSidebarCollapsed: collapsed });
  },

  collapsedGroupIds: [],
  toggleGroupCollapse: (groupId) =>
    set((state) => {
      const collapsed = state.collapsedGroupIds.includes(groupId)
        ? state.collapsedGroupIds.filter((id) => id !== groupId)
        : [...state.collapsedGroupIds, groupId];
      return { collapsedGroupIds: collapsed };
    }),

  theme:
    (typeof localStorage !== "undefined" &&
      (localStorage.getItem(STORAGE_KEYS.THEME) as "dark" | "light" | "system")) ||
    "system",
  resolvedTheme: (() => {
    if (typeof localStorage !== "undefined") {
      const stored = localStorage.getItem(STORAGE_KEYS.THEME) as "dark" | "light" | "system" | null;
      if (stored === "dark") return "dark";
      if (stored === "light") return "light";
    }
    // For "system" or no stored value, check OS preference
    if (typeof window !== "undefined" && window.matchMedia) {
      return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
    }
    return "dark";
  })(),
  setTheme: (theme) =>
    set(() => {
      if (typeof localStorage !== "undefined") {
        localStorage.setItem(STORAGE_KEYS.THEME, theme);
      }
      const resolved = resolveTheme(theme);
      if (typeof document !== "undefined") {
        document.documentElement.classList.toggle("dark", resolved === "dark");
        writeThemeCookie(resolved);
      }
      return { theme, resolvedTheme: resolved };
    }),
}));

/** Resolve "system" to actual dark/light based on OS preference */
function resolveTheme(theme: "dark" | "light" | "system"): "dark" | "light" {
  if (theme === "dark" || theme === "light") return theme;
  if (typeof window !== "undefined" && window.matchMedia) {
    return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  }
  return "dark";
}

// Listen for OS theme changes when in "system" mode
if (typeof window !== "undefined" && window.matchMedia) {
  const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
  mediaQuery.addEventListener("change", (e) => {
    const state = useAppStore.getState();
    if (state.theme === "system") {
      const resolved = e.matches ? "dark" : "light";
      document.documentElement.classList.toggle("dark", resolved === "dark");
      writeThemeCookie(resolved);
      useAppStore.setState({ resolvedTheme: resolved });
    }
  });
}
