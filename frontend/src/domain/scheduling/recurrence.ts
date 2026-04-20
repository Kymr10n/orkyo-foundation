import { toZonedTime, fromZonedTime } from "date-fns-tz";
import {
  addDays,
  addWeeks,
  addMonths,
  addYears,
  getDay,
} from "date-fns";
import type { OffTimeDefinition, OffTimeRange } from "./types";
import {
  RRULE_DAY_MAP,
  RRULE_FREQ,
  RRULE_FIELD,
  DAYS_PER_WEEK,
  MAX_CALC_ITERATIONS,
  type RRuleFrequency,
} from "../constants";

interface ParsedRule {
  freq: RRuleFrequency;
  interval: number;
  byDay: number[] | null;
  until: Date | null;
  count: number | null;
}

export function parseRRule(rule: string): ParsedRule {
  const parts = rule.split(";");
  let freq: RRuleFrequency | null = null;
  let interval = 1;
  let byDay: number[] | null = null;
  let until: Date | null = null;
  let count: number | null = null;

  for (const part of parts) {
    const [key, value] = part.split("=");
    switch (key) {
      case RRULE_FIELD.FREQ:
        if (
          value !== RRULE_FREQ.DAILY &&
          value !== RRULE_FREQ.WEEKLY &&
          value !== RRULE_FREQ.MONTHLY &&
          value !== RRULE_FREQ.YEARLY
        ) {
          throw new Error(`Unsupported RRULE frequency: ${value}`);
        }
        freq = value;
        break;
      case RRULE_FIELD.INTERVAL:
        interval = parseInt(value, 10);
        if (isNaN(interval) || interval < 1) {
          throw new Error(`Invalid RRULE INTERVAL: ${value}`);
        }
        break;
      case RRULE_FIELD.BYDAY:
        byDay = value.split(",").map((d) => {
          const mapped = RRULE_DAY_MAP[d.trim()];
          if (mapped === undefined) {
            throw new Error(`Unknown RRULE day: ${d}`);
          }
          return mapped;
        });
        break;
      case RRULE_FIELD.UNTIL: {
        // RRULE UNTIL format: "YYYYMMDD" — fixed-width positional parse
        const y = parseInt(value.slice(0, 4), 10);              
        const m = parseInt(value.slice(4, 6), 10) - 1;          
        const day = parseInt(value.slice(6, 8), 10);             
        until = new Date(Date.UTC(y, m, day, 23, 59, 59, 999));  
        break;
      }
      case RRULE_FIELD.COUNT:
        count = parseInt(value, 10);
        if (isNaN(count) || count < 1) {
          throw new Error(`Invalid RRULE COUNT: ${value}`);
        }
        break;
      default:
        throw new Error(
          `Unsupported RRULE clause: ${key}. Supported: FREQ, INTERVAL, BYDAY, UNTIL, COUNT`
        );
    }
  }

  if (!freq) {
    throw new Error("RRULE must include a FREQ clause");
  }

  return { freq, interval, byDay, until, count };
}

function advanceCursor(
  cursor: Date,
  freq: RRuleFrequency,
  interval: number
): Date {
  switch (freq) {
    case RRULE_FREQ.DAILY:
      return addDays(cursor, interval);
    case RRULE_FREQ.WEEKLY:
      return addWeeks(cursor, interval);
    case RRULE_FREQ.MONTHLY:
      return addMonths(cursor, interval);
    case RRULE_FREQ.YEARLY:
      return addYears(cursor, interval);
  }
}

/**
 * Expand a recurring off-time definition into concrete OffTimeRange values
 * within the given window. All calculations respect the site time zone.
 */
export function expandRecurrence(
  definition: OffTimeDefinition,
  windowStartMs: number,
  windowEndMs: number,
  timeZone: string
): OffTimeRange[] {
  if (!definition.isRecurring || !definition.recurrenceRule) {
    // Non-recurring: return as-is if it overlaps the window.
    if (definition.endMs <= windowStartMs || definition.startMs >= windowEndMs) {
      return [];
    }
    return [toRange(definition, definition.startMs, definition.endMs)];
  }

  const rule = parseRRule(definition.recurrenceRule);
  const durationMs = definition.endMs - definition.startMs;

  // Work in the site's local time zone so DST is handled correctly.
  const templateLocal = toZonedTime(definition.startMs, timeZone);
  const hours = templateLocal.getHours();
  const minutes = templateLocal.getMinutes();
  const seconds = templateLocal.getSeconds();
  const ms = templateLocal.getMilliseconds();

  const ranges: OffTimeRange[] = [];
  let cursor = toZonedTime(definition.startMs, timeZone);
  let emitted = 0;

  for (let i = 0; i < MAX_CALC_ITERATIONS; i++) {
    if (rule.until) {
      const cursorUtc = fromZonedTime(cursor, timeZone);
      if (cursorUtc.getTime() > rule.until.getTime()) break;
    }
    if (rule.count !== null && emitted >= rule.count) break;

    // For WEEKLY + BYDAY, expand each matching day in the week.
    const candidates: Date[] =
      rule.freq === RRULE_FREQ.WEEKLY && rule.byDay
        ? expandByDay(cursor, rule.byDay, hours, minutes, seconds, ms)
        : [cursor];

    for (const candidate of candidates) {
      if (rule.count !== null && emitted >= rule.count) break;

      const occStartUtc = fromZonedTime(candidate, timeZone).getTime();
      const occEndUtc = occStartUtc + durationMs;

      // Past the window — stop entirely.
      if (occStartUtc >= windowEndMs) {
        return ranges;
      }

      // Overlaps the window — include.
      if (occEndUtc > windowStartMs) {
        ranges.push(toRange(definition, occStartUtc, occEndUtc));
      }

      emitted++;
    }

    cursor = advanceCursor(cursor, rule.freq, rule.interval);
    // Preserve the template's local time (handles DST shifts).
    cursor.setHours(hours, minutes, seconds, ms);
  }

  return ranges;
}

function expandByDay(
  weekStart: Date,
  byDay: number[],
  hours: number,
  minutes: number,
  seconds: number,
  ms: number
): Date[] {
  const baseDay = getDay(weekStart);
  const sorted = [...byDay].sort((a, b) => a - b);
  return sorted.map((targetDay) => {
    let diff = targetDay - baseDay;
    if (diff < 0) diff += DAYS_PER_WEEK;
    const d = addDays(weekStart, diff);
    d.setHours(hours, minutes, seconds, ms);
    return d;
  });
}

function toRange(
  def: OffTimeDefinition,
  startMs: number,
  endMs: number
): OffTimeRange {
  return {
    id: def.id,
    startMs,
    endMs,
    title: def.title,
    spaceIds: def.appliesToAllSpaces ? null : def.spaceIds,
  };
}
