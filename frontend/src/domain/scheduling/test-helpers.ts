import type { EffectiveCalendar, SchedulingSettings } from "./types";
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
