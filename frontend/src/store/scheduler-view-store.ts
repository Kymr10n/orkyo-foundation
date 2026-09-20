import { create } from "zustand";

export type SchedulerScale = "year" | "month" | "week" | "day" | "hour";

/**
 * What the scheduler is looking at: the zoom level, the window it is centred on, and
 * the time cursor.
 *
 * **Why this is not URL state.** A shareable scheduler view is worth having, and the
 * repo already has `useTableUrlState` for exactly that kind of thing. It does not fit
 * here: `anchorTs` is a continuous pan value, rewritten about twenty times a second
 * while the user drags the timeline (see the memo note in `UtilizationPage`). Routing
 * that through `useSearchParams` means a router navigation per animation frame, which
 * costs more than the sharing is worth. `scale` alone could live in the URL, but a half
 * URL-encoded view is a second source of truth for one screen, not a shareable link.
 * If the view ever needs sharing, the answer is an explicit "copy link" action that
 * serialises this state on demand, not a write on every frame.
 *
 * Deliberately not persisted: a stale anchor from last week is worse than today.
 */
interface SchedulerViewState {
  scale: SchedulerScale;
  anchorTs: Date;
  timeCursorTs: Date;
  /**
   * Ephemeral drag order for the scheduler's space rows: the ids the user dragged,
   * in the order they left them, for this visit only.
   *
   * It is not server state and is deliberately not reconciled with one. The grid
   * sorts the rows it already has by this list and appends anything absent, so an
   * id for a deleted space is inert and a new space lands at the end. Nothing
   * persists it, so a reload returns to the server's order — which is the whole
   * reason it can stay this simple.
   */
  spaceOrder: string[];
  setScale: (scale: SchedulerScale) => void;
  setAnchorTs: (ts: Date) => void;
  setTimeCursorTs: (ts: Date) => void;
  setSpaceOrder: (order: string[]) => void;
}

export const useSchedulerViewStore = create<SchedulerViewState>((set) => ({
  scale: "week",
  anchorTs: new Date(),
  timeCursorTs: new Date(),
  spaceOrder: [],
  setScale: (scale) => set({ scale }),
  setAnchorTs: (anchorTs) => set({ anchorTs }),
  setTimeCursorTs: (timeCursorTs) => set({ timeCursorTs }),
  setSpaceOrder: (spaceOrder) => set({ spaceOrder }),
}));
