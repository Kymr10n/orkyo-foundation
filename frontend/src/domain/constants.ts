/**
 * Shared constants for the scheduling domain.
 *
 * All time-unit conversions derive from these base values so there is
 * exactly one source of truth. Import from here — never inline numeric
 * literals for time math.
 */

import type { DurationUnit } from "@/types/requests";

// ---------------------------------------------------------------------------
// Base time-unit conversions
// ---------------------------------------------------------------------------

export const MS_PER_SECOND = 1_000;
const SECONDS_PER_MINUTE = 60;
export const MINUTES_PER_HOUR = 60;
const HOURS_PER_DAY = 24;
export const DAYS_PER_WEEK = 7;
const DAYS_PER_MONTH = 30; // approximation used for duration estimates
const DAYS_PER_YEAR = 365; // approximation used for duration estimates

const MINUTES_PER_DAY = MINUTES_PER_HOUR * HOURS_PER_DAY;
const MINUTES_PER_WEEK = MINUTES_PER_DAY * DAYS_PER_WEEK;
const MINUTES_PER_MONTH = MINUTES_PER_DAY * DAYS_PER_MONTH;
const MINUTES_PER_YEAR = MINUTES_PER_DAY * DAYS_PER_YEAR;

export const MS_PER_MINUTE = SECONDS_PER_MINUTE * MS_PER_SECOND;
export const MS_PER_HOUR = MINUTES_PER_HOUR * MS_PER_MINUTE;
export const MS_PER_DAY = HOURS_PER_DAY * MS_PER_HOUR;
export const MS_PER_WEEK = DAYS_PER_WEEK * MS_PER_DAY;
const MS_PER_MONTH = DAYS_PER_MONTH * MS_PER_DAY;
const MS_PER_YEAR = DAYS_PER_YEAR * MS_PER_DAY;

// ---------------------------------------------------------------------------
// Duration-unit lookup tables (derived from base constants)
// ---------------------------------------------------------------------------

/** Minutes per duration unit — used for effort roll-ups in the request tree. */
export const DURATION_TO_MINUTES: Record<DurationUnit, number> = {
  minutes: 1,
  hours: MINUTES_PER_HOUR,
  days: MINUTES_PER_DAY,
  weeks: MINUTES_PER_WEEK,
  months: MINUTES_PER_MONTH,
  years: MINUTES_PER_YEAR,
};

/** Milliseconds per duration unit — used for scheduling calculations. */
export const DURATION_UNIT_MS: Record<DurationUnit, number> = {
  minutes: MS_PER_MINUTE,
  hours: MS_PER_HOUR,
  days: MS_PER_DAY,
  weeks: MS_PER_WEEK,
  months: MS_PER_MONTH,
  years: MS_PER_YEAR,
};

// ---------------------------------------------------------------------------
// Day-of-week constants (Date.getDay() values)
// ---------------------------------------------------------------------------

export const SUNDAY = 0;
export const SATURDAY = 6;

export function isWeekendDay(dow: number): boolean {
  return dow === SUNDAY || dow === SATURDAY;
}

// ---------------------------------------------------------------------------
// Validation error codes (request-tree validation)
// ---------------------------------------------------------------------------

export const ValidationCode = {
  LEAF_HAS_CHILDREN: "LEAF_HAS_CHILDREN",
  START_AFTER_END: "START_AFTER_END",
  BELOW_MIN_DURATION: "BELOW_MIN_DURATION",
  CHILD_BEFORE_CONTAINER_START: "CHILD_BEFORE_CONTAINER_START",
  CHILD_AFTER_CONTAINER_END: "CHILD_AFTER_CONTAINER_END",
} as const;

// ---------------------------------------------------------------------------
// RRULE constants
// ---------------------------------------------------------------------------

export const RRULE_DAY_MAP: Record<string, number> = {
  SU: SUNDAY,
  MO: 1,
  TU: 2,
  WE: 3,
  TH: 4,
  FR: 5,
  SA: SATURDAY,
};

export const RRULE_FREQ = {
  DAILY: "DAILY",
  WEEKLY: "WEEKLY",
  MONTHLY: "MONTHLY",
  YEARLY: "YEARLY",
} as const;

export type RRuleFrequency = (typeof RRULE_FREQ)[keyof typeof RRULE_FREQ];

// ---------------------------------------------------------------------------
// RRULE parsing field names
// ---------------------------------------------------------------------------

export const RRULE_FIELD = {
  FREQ: "FREQ",
  INTERVAL: "INTERVAL",
  BYDAY: "BYDAY",
  UNTIL: "UNTIL",
  COUNT: "COUNT",
} as const;

// ---------------------------------------------------------------------------
// Safety-limit iteration caps
// ---------------------------------------------------------------------------

export const MAX_DAY_ITERATIONS = 400;
export const MAX_CALC_ITERATIONS = 10_000;

// ---------------------------------------------------------------------------
// Rounding
// ---------------------------------------------------------------------------

export const DISPLAY_PRECISION = 100; // for Math.round(v * N) / N
