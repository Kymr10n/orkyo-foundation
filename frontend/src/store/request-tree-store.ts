import { create } from "zustand";

const STORAGE_KEY_EXPANDED_IDS = "requestTree.expandedIds";

function readExpandedIds(): Set<string> {
  try {
    const raw = localStorage.getItem(STORAGE_KEY_EXPANDED_IDS);
    if (!raw) return new Set<string>();
    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) return new Set<string>();
    return new Set(parsed.filter((id): id is string => typeof id === "string"));
  } catch {
    return new Set<string>();
  }
}

function writeExpandedIds(ids: Set<string>) {
  try {
    localStorage.setItem(STORAGE_KEY_EXPANDED_IDS, JSON.stringify([...ids]));
  } catch {
    // Ignore persistence failures (private mode/quota).
  }
}

interface RequestTreeState {
  /** IDs of expanded tree nodes */
  expandedIds: Set<string>;
  /** Currently selected request ID (for detail panel) */
  selectedId: string | null;

  toggle: (id: string) => void;
  expand: (id: string) => void;
  collapse: (id: string) => void;
  expandAll: (ids: string[]) => void;
  collapseAll: () => void;
  expandAncestors: (ids: string[]) => void;
  setSelectedId: (id: string | null) => void;
}

export const useRequestTreeStore = create<RequestTreeState>((set) => ({
  expandedIds: readExpandedIds(),
  selectedId: null,

  toggle: (id) =>
    set((state) => {
      const next = new Set(state.expandedIds);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      writeExpandedIds(next);
      return { expandedIds: next };
    }),

  expand: (id) =>
    set((state) => {
      if (state.expandedIds.has(id)) return state;
      const next = new Set(state.expandedIds);
      next.add(id);
      writeExpandedIds(next);
      return { expandedIds: next };
    }),

  collapse: (id) =>
    set((state) => {
      if (!state.expandedIds.has(id)) return state;
      const next = new Set(state.expandedIds);
      next.delete(id);
      writeExpandedIds(next);
      return { expandedIds: next };
    }),

  expandAll: (ids) =>
    set(() => {
      const next = new Set(ids);
      writeExpandedIds(next);
      return { expandedIds: next };
    }),

  collapseAll: () =>
    set(() => {
      const next = new Set<string>();
      writeExpandedIds(next);
      return { expandedIds: next };
    }),

  expandAncestors: (ids) =>
    set((state) => {
      const next = new Set(state.expandedIds);
      for (const id of ids) next.add(id);
      writeExpandedIds(next);
      return { expandedIds: next };
    }),

  setSelectedId: (id) => set({ selectedId: id }),
}));
