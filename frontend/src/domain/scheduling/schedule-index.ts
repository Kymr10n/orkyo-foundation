/**
 * Spatial index for the preview schedule.
 *
 * Groups entries by spaceId so overlap detection is O(n) per space
 * rather than O(n²) across the entire request set.
 *
 * All methods are pure — the index is immutable after construction.
 */

import type { PreviewEntry, PreviewSchedule } from "./schedule-model";

export interface ScheduleIndex {
  /** spaceId → entries in that space, sorted by startMs ascending */
  readonly bySpace: ReadonlyMap<string, readonly PreviewEntry[]>;
}

/** Build an index from a preview schedule. O(n log n). */
export function buildIndex(schedule: PreviewSchedule): ScheduleIndex {
  const bySpace = new Map<string, PreviewEntry[]>();

  for (const entry of schedule.values()) {
    if (!bySpace.has(entry.spaceId)) {
      bySpace.set(entry.spaceId, []);
    }
    bySpace.get(entry.spaceId)!.push(entry);
  }

  // Sort each space's entries by startMs for deterministic stacking order
  for (const entries of bySpace.values()) {
    entries.sort((a, b) => a.startMs - b.startMs || a.requestId.localeCompare(b.requestId));
  }

  return { bySpace };
}

/**
 * Returns all entries in the same space that overlap with `target`.
 * Uses the half-open interval definition: [start, end).
 * An entry does NOT overlap with itself.
 */
export function getOverlapping(index: ScheduleIndex, target: PreviewEntry): PreviewEntry[] {
  const spaceEntries = index.bySpace.get(target.spaceId);
  if (!spaceEntries) return [];

  const result: PreviewEntry[] = [];
  for (const entry of spaceEntries) {
    if (entry.requestId === target.requestId) continue;
    // [a,b) overlaps [c,d) iff a < d && c < b
    if (entry.startMs < target.endMs && target.startMs < entry.endMs) {
      result.push(entry);
    }
  }
  return result;
}

/**
 * Returns the 0-based stacking index for `target` within its space.
 * Entries are stacked in startMs order (earliest → index 0).
 * Ties broken by requestId for determinism.
 */
export function getStackIndex(index: ScheduleIndex, target: PreviewEntry): number {
  const overlapping = getOverlapping(index, target);
  let rank = 0;
  for (const entry of overlapping) {
    if (
      entry.startMs < target.startMs ||
      (entry.startMs === target.startMs && entry.requestId < target.requestId)
    ) {
      rank++;
    }
  }
  return rank;
}

/**
 * Returns the total overlap group size for `target` (includes self).
 * Used for row-height expansion.
 */
export function getOverlapGroupSize(index: ScheduleIndex, target: PreviewEntry): number {
  return getOverlapping(index, target).length + 1;
}

/**
 * Returns the maximum simultaneous overlap count for an entire space.
 * Used to compute the space row's total height.
 */
export function getMaxOverlapInSpace(index: ScheduleIndex, spaceId: string): number {
  const entries = index.bySpace.get(spaceId);
  if (!entries || entries.length === 0) return 1;

  let max = 1;
  for (const entry of entries) {
    const size = getOverlapGroupSize(index, entry);
    if (size > max) max = size;
  }
  return max;
}
