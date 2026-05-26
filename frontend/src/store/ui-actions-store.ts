/**
 * UI Actions store — typed replacement for the `window.dispatchEvent` pattern
 * the TopBar previously used to fire export/import/command-palette/tour
 * triggers across the tree.
 *
 * The store holds a per-action `tick` counter plus the most recent payload.
 * Consumers `useEffect` on the tick: every tick increment means a new fire,
 * regardless of whether the payload is identical to the previous one.
 *
 * This gives us:
 *   - Type-safe payloads (no `event as CustomEvent<T>` casts)
 *   - DevTools visibility via the standard Zustand store
 *   - Subscriptions scoped to React lifecycles (no global window leaks)
 */

import { create } from 'zustand';
import type { ExportContext, ExportFormat, ImportFormat } from '@foundation/src/lib/utils/import-export';

export interface ExportPayload {
  context: ExportContext;
  format: ExportFormat;
}

export interface ImportPayload {
  context: ExportContext;
  format: ImportFormat;
  file: File;
}

interface UiActionsState {
  // Counters bumped on each trigger
  exportTick: number;
  importTick: number;
  commandPaletteTick: number;
  tourTick: number;
  // Last payload for actions that carry data
  lastExport: ExportPayload | null;
  lastImport: ImportPayload | null;

  // Triggers
  triggerExport: (payload: ExportPayload) => void;
  triggerImport: (payload: ImportPayload) => void;
  openCommandPalette: () => void;
  openTour: () => void;
}

export const useUiActionsStore = create<UiActionsState>((set) => ({
  exportTick: 0,
  importTick: 0,
  commandPaletteTick: 0,
  tourTick: 0,
  lastExport: null,
  lastImport: null,

  triggerExport: (payload) =>
    set((s) => ({ exportTick: s.exportTick + 1, lastExport: payload })),
  triggerImport: (payload) =>
    set((s) => ({ importTick: s.importTick + 1, lastImport: payload })),
  openCommandPalette: () =>
    set((s) => ({ commandPaletteTick: s.commandPaletteTick + 1 })),
  openTour: () => set((s) => ({ tourTick: s.tourTick + 1 })),
}));
