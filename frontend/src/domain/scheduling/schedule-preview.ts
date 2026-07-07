/**
 * Preview schedule builder.
 *
 * Takes:
 *   1. The committed request list (query-cache truth)
 *   2. The current draft interaction (or null when idle)
 *
 * Returns a PreviewSchedule — the complete set of entries that the UI
 * should render, with the draft interaction's bounds overriding the
 * committed bounds for the affected request.
 *
 * These are pure functions with no side effects. Entry objects are reused
 * wherever possible: applyDraft returns the input schedule unchanged when
 * there is no draft, and otherwise replaces only the dragged entry's object —
 * so React.memo'd rows for untouched requests hold during a drag.
 */

import type { Request } from "@foundation/src/types/requests";
import type { DraftInteraction, PreviewEntry, PreviewSchedule } from "./schedule-model";
import { toScheduledEntry } from "./schedule-model";

/** Build the draft-free schedule from the committed request list. */
export function buildCommittedSchedule(committed: Request[]): PreviewSchedule {
  const preview: PreviewSchedule = new Map();

  for (const request of committed) {
    const entry = toScheduledEntry(request);
    if (!entry) continue;

    preview.set(entry.requestId, {
      requestId: entry.requestId,
      resourceId: entry.resourceId,
      startMs: entry.startMs,
      endMs: entry.endMs,
      name: entry.name,
      minimalDurationMs: entry.minimalDurationMs,
      isDraft: false,
    } satisfies PreviewEntry);
  }

  return preview;
}

/**
 * Overlay the draft interaction onto a committed schedule.
 *
 * Returns the input schedule itself when there is nothing to apply; otherwise
 * a new Map where only the dragged request's entry is a new object (all other
 * entries are shared by reference with the input).
 */
export function applyDraft(
  committed: PreviewSchedule,
  draft: DraftInteraction
): PreviewSchedule {
  if (!draft) return committed;
  const entry = committed.get(draft.requestId);
  if (!entry) return committed;

  const preview = new Map(committed);
  preview.set(entry.requestId, {
    requestId: entry.requestId,
    resourceId: draft.resourceId,
    startMs: draft.previewStartMs,
    endMs: draft.previewEndMs,
    name: entry.name,
    minimalDurationMs: entry.minimalDurationMs,
    isDraft: true,
  } satisfies PreviewEntry);
  return preview;
}

export function buildPreviewSchedule(
  committed: Request[],
  draft: DraftInteraction
): PreviewSchedule {
  return applyDraft(buildCommittedSchedule(committed), draft);
}
