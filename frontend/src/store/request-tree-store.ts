import { create } from "zustand";
import { persist } from "zustand/middleware";
import { STORAGE_KEYS } from "@foundation/src/constants/storage";

export type RequestViewMode = "tree" | "list";

/** What survives a reload. The selection does not: it names a row of the last visit. */
interface PersistedTree {
  expandedIds: string[];
  viewMode: RequestViewMode;
}

interface RequestTreeState {
  /** IDs of expanded tree nodes */
  expandedIds: Set<string>;
  /** Currently selected request ID (drives row highlight + keyboard anchor) */
  selectedId: string | null;
  /** Tree vs. list view mode on the Requests page */
  viewMode: RequestViewMode;

  toggle: (id: string) => void;
  expand: (id: string) => void;
  collapse: (id: string) => void;
  expandAll: (ids: string[]) => void;
  collapseAll: () => void;
  expandAncestors: (ids: string[]) => void;
  setSelectedId: (id: string | null) => void;
  setViewMode: (mode: RequestViewMode) => void;
}

export const useRequestTreeStore = create<RequestTreeState>()(
  persist(
    (set) => ({
      expandedIds: new Set<string>(),
      selectedId: null,
      viewMode: "tree",

      toggle: (id) =>
        set((state) => {
          const next = new Set(state.expandedIds);
          if (next.has(id)) next.delete(id);
          else next.add(id);
          return { expandedIds: next };
        }),

      expand: (id) =>
        set((state) => {
          if (state.expandedIds.has(id)) return state;
          const next = new Set(state.expandedIds);
          next.add(id);
          return { expandedIds: next };
        }),

      collapse: (id) =>
        set((state) => {
          if (!state.expandedIds.has(id)) return state;
          const next = new Set(state.expandedIds);
          next.delete(id);
          return { expandedIds: next };
        }),

      expandAll: (ids) =>
        set(() => {
          const next = new Set(ids);
          return { expandedIds: next };
        }),

      collapseAll: () =>
        set(() => {
          const next = new Set<string>();
          return { expandedIds: next };
        }),

      expandAncestors: (ids) =>
        set((state) => {
          const next = new Set(state.expandedIds);
          for (const id of ids) next.add(id);
          return { expandedIds: next };
        }),

      setSelectedId: (id) => set({ selectedId: id }),

      setViewMode: (mode) => set({ viewMode: mode }),
    }),
    {
      name: STORAGE_KEYS.REQUEST_TREE,
      // A Set does not survive JSON, so the ids travel as an array.
      partialize: (state): PersistedTree => ({ expandedIds: [...state.expandedIds], viewMode: state.viewMode }),
      merge: (persisted, current) => {
        const saved = (persisted ?? {}) as Partial<PersistedTree>;
        const ids = Array.isArray(saved.expandedIds)
          ? saved.expandedIds.filter((id): id is string => typeof id === "string")
          : [];
        return { ...current, expandedIds: new Set(ids), viewMode: saved.viewMode === "list" ? "list" : "tree" };
      },
    },
  ),
);
