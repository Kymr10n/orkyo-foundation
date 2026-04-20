import { addDays, startOfDay, isWeekend } from "date-fns";
import type { OffTimeRange } from "./types";

/**
 * Generates OffTimeRange entries for every Saturday and Sunday within the
 * given time window. Each weekend day is a separate range (full 24h).
 * Returns ranges sorted by startMs.
 */
export function generateWeekendRanges(
  windowStartMs: number,
  windowEndMs: number,
): OffTimeRange[] {
  const ranges: OffTimeRange[] = [];
  let cursor = startOfDay(new Date(windowStartMs));

  while (cursor.getTime() < windowEndMs) {
    if (isWeekend(cursor)) {
      const dayStart = cursor.getTime();
      const dayEnd = addDays(cursor, 1).getTime();
      ranges.push({
        id: `weekend-${dayStart}`,
        startMs: Math.max(dayStart, windowStartMs),
        endMs: Math.min(dayEnd, windowEndMs),
        title: "Weekend",
        spaceIds: null, // applies to all spaces
      });
    }
    cursor = addDays(cursor, 1);
  }

  return ranges;
}
