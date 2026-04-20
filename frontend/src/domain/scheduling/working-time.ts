import { toZonedTime } from "date-fns-tz";
import { startOfDay, addDays } from "date-fns";
import type { EffectiveCalendar, OffTimeRange } from "./types";
import {
  MAX_DAY_ITERATIONS,
  MS_PER_MINUTE,
  MS_PER_SECOND,
  MINUTES_PER_HOUR,
  isWeekendDay,
} from "../constants";

/**
 * Parse "HH:mm" into minutes since midnight.
 */
function parseHHmm(hhmm: string): number {
  const [h, m] = hhmm.split(":").map(Number);
  return h * MINUTES_PER_HOUR + m;
}

/**
 * Pre-parsed working hours boundaries (minutes since midnight).
 */
interface WorkingHoursBounds {
  start: number;
  end: number;
}

function getWorkingHoursBounds(calendar: EffectiveCalendar): WorkingHoursBounds {
  return {
    start: parseHHmm(calendar.settings.workingDayStart),
    end: parseHHmm(calendar.settings.workingDayEnd),
  };
}

/**
 * Get the local date string "YYYY-MM-DD" for an epoch ms in the given time zone.
 */
function localDateStr(epochMs: number, timeZone: string): string {
  const z = toZonedTime(epochMs, timeZone);
  const y = z.getFullYear();
  const m = String(z.getMonth() + 1).padStart(2, "0");
  const d = String(z.getDate()).padStart(2, "0");
  return `${y}-${m}-${d}`;
}

/**
 * Get local minutes since midnight for an epoch ms in the given time zone.
 */
function localMinuteOfDay(epochMs: number, timeZone: string): number {
  const zoned = toZonedTime(epochMs, timeZone);
  return zoned.getHours() * MINUTES_PER_HOUR + zoned.getMinutes();
}

/**
 * Get the day-of-week (0=Sun, 6=Sat) for an epoch ms in the given time zone.
 */
function localDayOfWeek(epochMs: number, timeZone: string): number {
  return toZonedTime(epochMs, timeZone).getDay();
}

/**
 * Does this off-time range apply to the given space?
 */
function offTimeAppliesToSpace(r: OffTimeRange, spaceId: string | null): boolean {
  return r.spaceIds === null || spaceId === null || r.spaceIds.includes(spaceId);
}

/**
 * Find the first off-time range that contains the given instant
 * and applies to the given space.
 */
function findContainingOffTime(
  offTimeRanges: readonly OffTimeRange[],
  epochMs: number,
  spaceId: string | null
): OffTimeRange | undefined {
  return offTimeRanges.find(
    (r) =>
      epochMs >= r.startMs &&
      epochMs < r.endMs &&
      offTimeAppliesToSpace(r, spaceId)
  );
}

/**
 * Find the next off-time range that starts at or after the given instant
 * and applies to the given space, before dayEndMs.
 */
function findNextOffTimeInDay(
  offTimeRanges: readonly OffTimeRange[],
  epochMs: number,
  dayEndMs: number,
  spaceId: string | null
): OffTimeRange | undefined {
  let earliest: OffTimeRange | undefined;
  for (const r of offTimeRanges) {
    if (r.startMs < dayEndMs && r.startMs >= epochMs && r.endMs > epochMs) {
      if (offTimeAppliesToSpace(r, spaceId)) {
        if (!earliest || r.startMs < earliest.startMs) {
          earliest = r;
        }
      }
    }
  }
  return earliest;
}

/**
 * Is the given instant within working time for the given calendar and space?
 *
 * Check order: off-times -> holidays -> weekends -> working hours.
 */
export function isWorkingTime(
  calendar: EffectiveCalendar,
  epochMs: number,
  spaceId: string | null
): boolean {
  const { settings } = calendar;

  // 1. Off-time check
  if (findContainingOffTime(calendar.offTimeRanges, epochMs, spaceId)) {
    return false;
  }

  const tz = settings.timeZone;

  // 2. Public holiday check
  if (settings.publicHolidaysEnabled) {
    const dateStr = localDateStr(epochMs, tz);
    if (calendar.holidays.has(dateStr)) {
      return false;
    }
  }

  // 3. Weekend check
  if (settings.weekendsEnabled) {
    const dow = localDayOfWeek(epochMs, tz);
    if (isWeekendDay(dow)) {
      return false;
    }
  }

  // 4. Working hours check
  if (settings.workingHoursEnabled) {
    const minute = localMinuteOfDay(epochMs, tz);
    const bounds = getWorkingHoursBounds(calendar);
    if (minute < bounds.start || minute >= bounds.end) {
      return false;
    }
  }

  return true;
}

/**
 * Return the epoch ms of the next working-time instant at or after epochMs.
 * Throws if no working time is found within MAX_ITERATIONS day advances.
 */
export function nextWorkingStart(
  calendar: EffectiveCalendar,
  epochMs: number,
  spaceId: string | null
): number {
  const { settings } = calendar;
  const tz = settings.timeZone;

  let cursor = epochMs;

  for (let i = 0; i < MAX_DAY_ITERATIONS; i++) {
    // Skip past any containing off-time.
    const offTime = findContainingOffTime(calendar.offTimeRanges, cursor, spaceId);
    if (offTime) {
      cursor = offTime.endMs;
      continue;
    }

    // Skip holidays.
    if (settings.publicHolidaysEnabled) {
      const dateStr = localDateStr(cursor, tz);
      if (calendar.holidays.has(dateStr)) {
        cursor = dayStartMs(cursor, tz, 1);
        continue;
      }
    }

    // Skip weekends.
    if (settings.weekendsEnabled) {
      const dow = localDayOfWeek(cursor, tz);
      if (isWeekendDay(dow)) {
        cursor = dayStartMs(cursor, tz, 1);
        continue;
      }
    }

    // Check working hours.
    if (settings.workingHoursEnabled) {
      const minute = localMinuteOfDay(cursor, tz);
      const bounds = getWorkingHoursBounds(calendar);

      if (minute >= bounds.end) {
        cursor = dayStartMs(cursor, tz, 1);
        continue;
      }

      if (minute < bounds.start) {
        cursor = cursor + (bounds.start - minute) * MS_PER_MINUTE;
        const zoned = toZonedTime(cursor, tz);
        cursor = cursor - zoned.getSeconds() * MS_PER_SECOND - zoned.getMilliseconds();
        continue;
      }
    }

    // All individual checks passed — cursor is in working time.
    return cursor;
  }

  throw new Error("No working time found within search range");
}

/**
 * Get the end of the current working segment starting at epochMs.
 * Returns the earliest of: working day end, next off-time start within the day.
 * Assumes epochMs is already in working time.
 */
export function workingSegmentEnd(
  calendar: EffectiveCalendar,
  epochMs: number,
  spaceId: string | null
): number {
  const { settings } = calendar;
  const tz = settings.timeZone;

  let segEnd: number;

  // Working day end boundary.
  if (settings.workingHoursEnabled) {
    const minute = localMinuteOfDay(epochMs, tz);
    const bounds = getWorkingHoursBounds(calendar);
    segEnd = epochMs + (bounds.end - minute) * MS_PER_MINUTE;
  } else {
    segEnd = dayStartMs(epochMs, tz, 1);
  }

  // Next off-time start within segment.
  const nextOff = findNextOffTimeInDay(
    calendar.offTimeRanges,
    epochMs,
    segEnd,
    spaceId
  );
  if (nextOff && nextOff.startMs > epochMs) {
    segEnd = Math.min(segEnd, nextOff.startMs);
  }

  return segEnd;
}

/**
 * Get epoch ms for start-of-day (+ dayOffset days) in the given time zone.
 * dayOffset=0 → start of current day, dayOffset=1 → start of next day.
 */
function dayStartMs(epochMs: number, timeZone: string, dayOffset = 0): number {
  const zoned = toZonedTime(epochMs, timeZone);
  const target = dayOffset === 0 ? startOfDay(zoned) : startOfDay(addDays(zoned, dayOffset));
  return epochMs + (target.getTime() - zoned.getTime());
}
