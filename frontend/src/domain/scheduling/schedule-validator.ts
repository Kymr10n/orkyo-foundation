/**
 * Pure validation engine — the in-flight draft overlay only.
 *
 * evaluateSchedule(schedule, existingIndex?) → ValidationResult
 *
 * Rules evaluated (in order):
 *   1. Below minimum duration
 *   2. Overlap with another entry in the same space
 *
 * Committed bookings' conflicts come from the backend (the single source of
 * truth, via the tenant-wide conflicts registry). This client check exists only
 * so the grid can give instant overlap/duration feedback for the bar being
 * dragged, before the mutation commits and the registry refetches. It is
 * deliberately capacity-agnostic (Exclusive semantics: any overlap is flagged).
 *
 * This function is side-effect-free and produces a stable result for any
 * given preview schedule. It must never be called inside a store action.
 */

import type { Conflict } from "@foundation/src/types/requests";
import { buildIndex, getOverlapping } from "./schedule-index";
import type { ScheduleIndex } from "./schedule-index";
import type { PreviewEntry, PreviewSchedule, ValidationResult } from "./schedule-model";

/**
 * Evaluate the client-side rules for a single entry against an index.
 * Used by the grid to validate only the entries affected by an in-flight
 * draft instead of re-running the whole windowed schedule per pointer frame.
 */
export function evaluateEntry(entry: PreviewEntry, index: ScheduleIndex): Conflict[] {
  const conflicts: Conflict[] = [];

  // --- Rule 1: duration below minimum ---
  const actualDurationMs = entry.endMs - entry.startMs;
  if (actualDurationMs < entry.minimalDurationMs) {
    conflicts.push({
      id: `${entry.requestId}-below-min-duration`,
      kind: "below_min_duration",
      severity: "error",
      message: `Duration is below the required minimum`,
    });
  }

  // --- Rule 2: overlap (any overlap in the same space is a conflict) ---
  for (const other of getOverlapping(index, entry)) {
    conflicts.push({
      id: `${entry.requestId}-overlap-${other.requestId}`,
      kind: "overlap",
      severity: "error",
      peerRequestId: other.requestId,
      message: `Overlaps with "${other.name}" in the same space`,
    });
  }

  return conflicts;
}

export function evaluateSchedule(
  schedule: PreviewSchedule,
  existingIndex?: ScheduleIndex,
): ValidationResult {
  const result: ValidationResult = new Map();
  const index = existingIndex ?? buildIndex(schedule);

  for (const entry of schedule.values()) {
    const conflicts = evaluateEntry(entry, index);
    if (conflicts.length > 0) {
      result.set(entry.requestId, conflicts);
    }
  }

  return result;
}

/** Convenience: does a given request have any conflicts? */
export function hasConflicts(result: ValidationResult, requestId: string): boolean {
  const conflicts = result.get(requestId);
  return conflicts !== undefined && conflicts.length > 0;
}

/** Convenience: get all conflicts across all requests (flat list). */
export function getAllConflicts(result: ValidationResult): Conflict[] {
  const all: Conflict[] = [];
  for (const conflicts of result.values()) {
    all.push(...conflicts);
  }
  return all;
}
