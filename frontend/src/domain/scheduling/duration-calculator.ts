import type { EffectiveCalendar } from "./types";
import { isWorkingTime, nextWorkingStart, workingSegmentEnd } from "./working-time";
import { MAX_CALC_ITERATIONS } from "../constants";

/**
 * Count how much working time (ms) is contained within [startMs, endMs).
 */
export function computeWorkingDuration(
  calendar: EffectiveCalendar,
  startMs: number,
  endMs: number,
  spaceId: string | null
): number {
  if (endMs <= startMs) return 0;

  let cursor = startMs;
  let total = 0;

  for (let i = 0; i < MAX_CALC_ITERATIONS; i++) {
    if (cursor >= endMs) break;

    // If not in working time, advance to next working start.
    if (!isWorkingTime(calendar, cursor, spaceId)) {
      cursor = nextWorkingStart(calendar, cursor, spaceId);
      if (cursor >= endMs) break;
    }

    const segEnd = Math.min(workingSegmentEnd(calendar, cursor, spaceId), endMs);
    total += segEnd - cursor;
    cursor = segEnd;
  }

  return total;
}

