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

/**
 * What a currently-mounted page can export (and, separately, import).
 *
 * The registry is the single source of truth for the TopBar's buttons: a page
 * that registers a handler IS the offer, so the two can no longer drift. The
 * TopBar used to infer the context from `location.pathname`, which silently
 * broke every time a route moved — and could never name a tenant-defined
 * resource type at all.
 */
export interface ExportCapability {
  label: string;
  description: string;
  formats: ExportFormat[];
}

export interface ImportCapability {
  formats: ImportFormat[];
}

/**
 * What a currently-mounted page can offer as a live calendar subscription.
 *
 * Separate from {@link ExportCapability} rather than a format on it: an export
 * hands back a file and is done, so the TopBar relays a format to the page and
 * closes. A subscription is a conversation — create, reveal the address once,
 * copy, revoke — owned entirely by its own dialog. There is nothing to relay
 * back, so this registry carries no formats and needs no tick.
 */
export interface CalendarFeedCapability {
  label: string;
  description: string;
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
  /**
   * Live capabilities, keyed by context. Insertion-ordered (Map semantics), so
   * the most recently mounted registrant wins when a tab registers inside a
   * page that also registers — the inner, more specific one is what the user
   * is looking at.
   */
  exportRegistry: Map<ExportContext, ExportCapability>;
  importRegistry: Map<ExportContext, ImportCapability>;
  calendarFeedRegistry: Map<ExportContext, CalendarFeedCapability>;

  // Triggers
  triggerExport: (payload: ExportPayload) => void;
  triggerImport: (payload: ImportPayload) => void;
  openCommandPalette: () => void;
  openTour: () => void;

  // Registration (called from the useExportHandler / useImportHandler effects)
  registerExport: (context: ExportContext, capability: ExportCapability) => void;
  unregisterExport: (context: ExportContext) => void;
  registerImport: (context: ExportContext, capability: ImportCapability) => void;
  unregisterImport: (context: ExportContext) => void;
  registerCalendarFeed: (context: ExportContext, capability: CalendarFeedCapability) => void;
  unregisterCalendarFeed: (context: ExportContext) => void;
}

/** Removes a key without mutating the stored Map (Zustand needs a new reference). */
function withoutKey<V>(map: Map<ExportContext, V>, context: ExportContext): Map<ExportContext, V> {
  if (!map.has(context)) return map;
  const next = new Map(map);
  next.delete(context);
  return next;
}

export const useUiActionsStore = create<UiActionsState>((set) => ({
  exportTick: 0,
  importTick: 0,
  commandPaletteTick: 0,
  tourTick: 0,
  lastExport: null,
  lastImport: null,
  exportRegistry: new Map(),
  importRegistry: new Map(),
  calendarFeedRegistry: new Map(),

  triggerExport: (payload) =>
    set((s) => ({ exportTick: s.exportTick + 1, lastExport: payload })),
  triggerImport: (payload) =>
    set((s) => ({ importTick: s.importTick + 1, lastImport: payload })),
  openCommandPalette: () =>
    set((s) => ({ commandPaletteTick: s.commandPaletteTick + 1 })),
  openTour: () => set((s) => ({ tourTick: s.tourTick + 1 })),

  // Re-registering an existing context deletes first, so the re-inserted entry
  // moves to the end and stays the active one.
  registerExport: (context, capability) =>
    set((s) => ({ exportRegistry: new Map(withoutKey(s.exportRegistry, context)).set(context, capability) })),
  unregisterExport: (context) =>
    set((s) => ({ exportRegistry: withoutKey(s.exportRegistry, context) })),
  registerImport: (context, capability) =>
    set((s) => ({ importRegistry: new Map(withoutKey(s.importRegistry, context)).set(context, capability) })),
  unregisterImport: (context) =>
    set((s) => ({ importRegistry: withoutKey(s.importRegistry, context) })),
  registerCalendarFeed: (context, capability) =>
    set((s) => ({ calendarFeedRegistry: new Map(withoutKey(s.calendarFeedRegistry, context)).set(context, capability) })),
  unregisterCalendarFeed: (context) =>
    set((s) => ({ calendarFeedRegistry: withoutKey(s.calendarFeedRegistry, context) })),
}));

/** The last-inserted entry, i.e. the most recently registered — see the registry docs above. */
function lastEntry<V>(map: Map<ExportContext, V>): [ExportContext, V] | null {
  let last: [ExportContext, V] | null = null;
  for (const entry of map) last = entry;
  return last;
}

/**
 * The capability the TopBar acts on: the most recently registered one.
 * Returns null when nothing on screen can export.
 */
export function selectActiveExport(
  state: Pick<UiActionsState, 'exportRegistry' | 'importRegistry'>,
): { context: ExportContext; capability: ExportCapability; importFormats: ImportFormat[] } | null {
  const last = lastEntry(state.exportRegistry);
  if (!last) return null;
  const [context, capability] = last;
  return { context, capability, importFormats: state.importRegistry.get(context)?.formats ?? [] };
}

/**
 * The calendar subscription the TopBar offers: the most recently registered one.
 * Returns null when nothing on screen has a schedule worth subscribing to.
 */
export function selectActiveCalendarFeed(
  state: Pick<UiActionsState, 'calendarFeedRegistry'>,
): { context: ExportContext; capability: CalendarFeedCapability } | null {
  const last = lastEntry(state.calendarFeedRegistry);
  if (!last) return null;
  const [context, capability] = last;
  return { context, capability };
}
