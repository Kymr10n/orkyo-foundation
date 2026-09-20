import type { EffectiveCalendar, OffTimeRange, SchedulingSettings } from "./types";
import { MS_PER_HOUR } from "../constants";

export const TZ = "Europe/Berlin";
export const HOUR = MS_PER_HOUR;

export function utc(iso: string): number {
  return new Date(iso).getTime();
}

export function makeSettings(overrides: Partial<SchedulingSettings> = {}): SchedulingSettings {
  return {
    siteId: "site-1",
    timeZone: TZ,
    workingHoursEnabled: true,
    workingDayStart: "08:00",
    workingDayEnd: "18:00",
    weekendsEnabled: true,
    publicHolidaysEnabled: false,
    publicHolidayRegion: null,
    ...overrides,
  };
}

export function makeCal(overrides: Partial<EffectiveCalendar> = {}): EffectiveCalendar {
  return {
    settings: makeSettings(),
    offTimeRanges: [],
    holidays: new Set(),
    ...overrides,
  };
}

/**
 * An off-time range, the shape `UtilizationPage` builds from availability events and weekends.
 *
 * Four suites hand-rolled this literal, each slightly differently, and none of them combined a
 * range with a coarse bucket — which is how a month-scale bug reached production. One factory so
 * a test is cheap to write at any scale.
 */
export function makeOffTimeRange(
  startISO: string,
  endISO: string,
  resourceIds: string[] | null = null,
): OffTimeRange {
  return {
    id: `off-${startISO}-${endISO}`,
    title: "Off",
    startMs: new Date(startISO).getTime(),
    endMs: new Date(endISO).getTime(),
    resourceIds,
  };
}
