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
 * This is a pure function with no side effects.
 */

import type { Request } from "@foundation/src/types/requests";
import type { DraftInteraction, PreviewEntry, PreviewSchedule } from "./schedule-model";
import { toScheduledEntry } from "./schedule-model";

export function buildPreviewSchedule(
  committed: Request[],
  draft: DraftInteraction
): PreviewSchedule {
  const preview: PreviewSchedule = new Map();

  for (const request of committed) {
    const entry = toScheduledEntry(request);
    if (!entry) continue;

    // Apply draft override if this is the request being interacted with
    if (draft?.requestId === entry.requestId) {
      preview.set(entry.requestId, {
        requestId: entry.requestId,
        spaceId: draft.spaceId,
        startMs: draft.previewStartMs,
        endMs: draft.previewEndMs,
        name: entry.name,
        minimalDurationMs: entry.minimalDurationMs,
        isDraft: true,
      } satisfies PreviewEntry);
    } else {
      preview.set(entry.requestId, {
        requestId: entry.requestId,
        spaceId: entry.spaceId,
        startMs: entry.startMs,
        endMs: entry.endMs,
        name: entry.name,
        minimalDurationMs: entry.minimalDurationMs,
        isDraft: false,
      } satisfies PreviewEntry);
    }
  }

  return preview;
}
