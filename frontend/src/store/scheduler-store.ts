/**
 * Scheduler interaction store (Zustand).
 *
 * Stores ONLY:
 *   - The draft interaction (null when idle)
 *
 * Rules (enforced by architecture):
 *   - NEVER store validation results here
 *   - NEVER compute validation inside actions
 *   - buildPreviewSchedule() and evaluateSchedule() are called in render,
 *     not in store actions
 *
 * Interaction flow (state-machine):
 *   1. startResize()       → set draft (phase: "active")
 *   2. updateResize()      → update draft preview bounds on every pointer move
 *   3. [render calls buildPreviewSchedule() + evaluateSchedule()]
 *   4. commitResize()      → freeze draft (phase: "committing"), return final bounds
 *      Caller fires the API mutation; draft STAYS ALIVE to prevent snap-back.
 *   5. finalizeDraft(id)   → clear draft only if it matches the given requestId
 *      Called from the mutation's onSettled — cache is authoritative at that point.
 *   6. cancelResize()      → clear draft (rollback, no API call)
 */

import { create } from "zustand";
import type { DraftInteraction, DraftResize, ResizeEdge } from "@/domain/scheduling/schedule-model";

/** Minimum pixel delta before a resize gesture becomes "active". */
export const RESIZE_MOVE_THRESHOLD_PX = 5;

/** Absolute floor on any scheduled duration (1 minute). */
export const MIN_DURATION_FLOOR_MS = 60_000;

interface SchedulerState {
  draft: DraftInteraction;

  startResize: (params: {
    requestId: string;
    spaceId: string;
    edge: ResizeEdge;
    committedStartMs: number;
    committedEndMs: number;
  }) => void;

  updateResize: (previewStartMs: number, previewEndMs: number) => void;

  /** Freeze draft and return final bounds. Draft stays alive (phase → "committing"). */
  commitResize: () => { startMs: number; endMs: number } | null;

  /** Clear draft if it matches requestId. Safe against stale onSettled callbacks. */
  finalizeDraft: (requestId: string) => void;

  cancelResize: () => void;
}

export const useSchedulerStore = create<SchedulerState>((set, get) => ({
  draft: null,

  startResize({ requestId, spaceId, edge, committedStartMs, committedEndMs }) {
    set({
      draft: {
        kind: "resize",
        phase: "active",
        requestId,
        spaceId,
        edge,
        committedStartMs,
        committedEndMs,
        previewStartMs: committedStartMs,
        previewEndMs: committedEndMs,
      } satisfies DraftResize,
    });
  },

  updateResize(previewStartMs, previewEndMs) {
    const { draft } = get();
    if (draft?.kind !== "resize" || draft.phase !== "active") return;
    set({ draft: { ...draft, previewStartMs, previewEndMs } });
  },

  commitResize() {
    const { draft } = get();
    if (draft?.kind !== "resize" || draft.phase !== "active") return null;
    const result = { startMs: draft.previewStartMs, endMs: draft.previewEndMs };
    set({ draft: { ...draft, phase: "committing" } });
    return result;
  },

  finalizeDraft(requestId) {
    const { draft } = get();
    if (draft?.requestId === requestId) {
      set({ draft: null });
    }
  },

  cancelResize() {
    set({ draft: null });
  },
}));
