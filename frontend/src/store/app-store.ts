import { type User } from "@foundation/src/types/auth";
import { type Conflict } from "@foundation/src/types/requests";
import { STORAGE_KEYS } from "@foundation/src/constants/storage";
import { COOKIE_NAMES } from "@foundation/src/constants/http";
import { create } from "zustand";

interface AppState {
  // Authentication
  user: User | null;
  isAuthLoading: boolean;
  setUser: (user: User | null) => void;
  setIsAuthLoading: (loading: boolean) => void;

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
  selectedSpaceId: string | null;
  selectedJobId: string | null;
  selectedRequestId: string | null;
  setSelectedSpaceId: (id: string | null) => void;
  setSelectedJobId: (id: string | null) => void;
  setSelectedRequestId: (id: string | null) => void;

  // Space row ordering
  spaceOrder: string[];
  setSpaceOrder: (order: string[]) => void;

  // Conflicts tracking
  conflicts: Map<string, Conflict[]>; // requestId -> conflicts
  setConflicts: (requestId: string, conflicts: Conflict[]) => void;
  clearConflicts: (requestId: string) => void;
  getAllConflicts: () => Conflict[];

  // Drawer state
  isDetailsDrawerOpen: boolean;
  setIsDetailsDrawerOpen: (open: boolean) => void;

  // Floorplan collapse state (v3)
  isFloorplanCollapsed: boolean;
  setIsFloorplanCollapsed: (collapsed: boolean) => void;

  // Sidebar collapse state
  isSidebarCollapsed: boolean;
  setIsSidebarCollapsed: (collapsed: boolean) => void;

  // Right panel tab state (deprecated in v3, but keeping for now)
  rightPanelTab: "details" | "floorplan";
  setRightPanelTab: (tab: "details" | "floorplan") => void;

  // Space groups collapse state
  collapsedGroupIds: Set<string>;
  toggleGroupCollapse: (groupId: string) => void;

  // Theme
  theme: "dark" | "light" | "system";
  resolvedTheme: "dark" | "light";
  setTheme: (theme: "dark" | "light" | "system") => void;
}

export const useAppStore = create<AppState>((set) => ({
  // Authentication
  user: null,
  isAuthLoading: false,
  setUser: (user) => set({ user }),
  setIsAuthLoading: (loading) => set({ isAuthLoading: loading }),

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

  selectedSpaceId: null,
  selectedJobId: null,
  selectedRequestId: null,
  setSelectedSpaceId: (id) =>
    set({ selectedSpaceId: id, isDetailsDrawerOpen: !!id }),
  setSelectedJobId: (id) =>
    set({ selectedJobId: id, isDetailsDrawerOpen: !!id }),
  setSelectedRequestId: (id) =>
    set({ selectedRequestId: id, isDetailsDrawerOpen: !!id }),

  spaceOrder: [],
  setSpaceOrder: (order) => set({ spaceOrder: order }),

  conflicts: new Map(),
  setConflicts: (requestId, conflicts) =>
    set((state) => {
      const newConflicts = new Map(state.conflicts);
      if (conflicts.length > 0) {
        newConflicts.set(requestId, conflicts);
      } else {
        newConflicts.delete(requestId);
      }
      return { conflicts: newConflicts };
    }),
  clearConflicts: (requestId) =>
    set((state) => {
      const newConflicts = new Map(state.conflicts);
      newConflicts.delete(requestId);
      return { conflicts: newConflicts };
    }),
  getAllConflicts: () => {
    const state = useAppStore.getState();
    const allConflicts: Conflict[] = [];
    state.conflicts.forEach((conflicts) => {
      allConflicts.push(...conflicts);
    });
    return allConflicts;
  },

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

  rightPanelTab: "floorplan", // Default to floorplan
  setRightPanelTab: (tab) => set({ rightPanelTab: tab }),

  collapsedGroupIds: new Set<string>(),
  toggleGroupCollapse: (groupId) =>
    set((state) => {
      const newSet = new Set(state.collapsedGroupIds);
      if (newSet.has(groupId)) {
        newSet.delete(groupId);
      } else {
        newSet.add(groupId);
      }
      return { collapsedGroupIds: newSet };
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
        // Share resolved theme with Keycloak via cookie
        document.cookie = `${COOKIE_NAMES.THEME}=${resolved};path=/;max-age=31536000;SameSite=Lax;Secure`;
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
      document.cookie = `${COOKIE_NAMES.THEME}=${resolved};path=/;max-age=31536000;SameSite=Lax;Secure`;
      useAppStore.setState({ resolvedTheme: resolved });
    }
  });
}
