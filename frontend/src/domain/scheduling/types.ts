/** Per-site scheduling configuration. */
export interface SchedulingSettings {
  siteId: string;
  /** IANA time zone, e.g. "Europe/Berlin" */
  timeZone: string;
  workingHoursEnabled: boolean;
  /** "HH:mm" in site-local time, e.g. "08:00" */
  workingDayStart: string;
  /** "HH:mm" in site-local time, e.g. "18:00" */
  workingDayEnd: string;
  /** When true, Saturday and Sunday are excluded from working time. */
  weekendsEnabled: boolean;
  publicHolidaysEnabled: boolean;
  /** ISO 3166-1 alpha-2, e.g. "DE". Required when publicHolidaysEnabled is true. */
  publicHolidayRegion: string | null;
}

export type OffTimeType = "holiday" | "maintenance" | "custom";

export const OFF_TIME_TYPE_LABELS: Record<OffTimeType, string> = {
  holiday: "Holiday",
  maintenance: "Maintenance",
  custom: "Custom",
};

/** Persistent off-time definition (may be recurring). */
export interface OffTimeDefinition {
  id: string;
  siteId: string;
  title: string;
  type: OffTimeType;
  appliesToAllSpaces: boolean;
  spaceIds: string[];
  /** Epoch ms, inclusive. */
  startMs: number;
  /** Epoch ms, exclusive. */
  endMs: number;
  isRecurring: boolean;
  /** RFC 5545 RRULE subset, e.g. "FREQ=WEEKLY;BYDAY=MO". Null when not recurring. */
  recurrenceRule: string | null;
  enabled: boolean;
}

/** A concrete non-working time range (already expanded from recurrence). */
export interface OffTimeRange {
  id: string;
  /** Epoch ms, inclusive. */
  startMs: number;
  /** Epoch ms, exclusive. */
  endMs: number;
  title: string;
  /** Null means applies to all spaces. */
  spaceIds: string[] | null;
}

/** Compiled calendar for a site — the single immutable object passed to all calculations. */
export interface EffectiveCalendar {
  settings: SchedulingSettings;
  /** Expanded and sorted by startMs. */
  offTimeRanges: readonly OffTimeRange[];
  /** "YYYY-MM-DD" strings for O(1) lookup. */
  holidays: ReadonlySet<string>;
}
