/**
 * Pure validation engine.
 *
 * evaluateSchedule(schedule, spaceCapacities?) → ValidationResult
 *
 * Rules evaluated (in order):
 *   1. Below minimum duration
 *   2. Overlap with another entry in the same space (capacity=1)
 *      OR capacity exceeded (capacity>1, concurrent allocations > capacity)
 *
 * This function is side-effect-free and produces a stable result for any
 * given preview schedule. It must never be called inside a store action.
 */

import type { Conflict } from "@/types/requests";
import { buildIndex, getOverlapping } from "./schedule-index";
import type { PreviewSchedule, ValidationResult } from "./schedule-model";

/** spaceId → max concurrent allocations allowed (default 1) */
type SpaceCapacityMap = ReadonlyMap<string, number>;

export function evaluateSchedule(
  schedule: PreviewSchedule,
  spaceCapacities?: SpaceCapacityMap,
): ValidationResult {
  const result: ValidationResult = new Map();
  const index = buildIndex(schedule);

  for (const entry of schedule.values()) {
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

    // --- Rule 2: overlap / capacity ---
    const overlapping = getOverlapping(index, entry);
    const capacity = spaceCapacities?.get(entry.spaceId) ?? 1;

    if (capacity <= 1) {
      // Classic behaviour: any overlap is a conflict
      for (const other of overlapping) {
        conflicts.push({
          id: `${entry.requestId}-overlap-${other.requestId}`,
          kind: "overlap",
          severity: "error",
          message: `Overlaps with "${other.name}" in the same space`,
        });
      }
    } else {
      // Capacity > 1: only flag when concurrent count exceeds capacity.
      // concurrent = overlapping.length + 1 (self)
      if (overlapping.length + 1 > capacity) {
        conflicts.push({
          id: `${entry.requestId}-capacity-exceeded`,
          kind: "capacity_exceeded",
          severity: "error",
          message: `Space capacity exceeded (${overlapping.length + 1}/${capacity} concurrent allocations)`,
        });
      }
    }

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
