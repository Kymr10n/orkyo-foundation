import type { SchedulingSettings } from "@foundation/src/domain/scheduling/types";
import { apiGet, apiPut, apiDelete } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

// ── Wire types (match backend JSON exactly) ─────────────────────

interface SchedulingSettingsWire {
  id: string;
  siteId: string;
  timeZone: string;
  workingHoursEnabled: boolean;
  workingDayStart: string; // "HH:mm:ss"
  workingDayEnd: string;   // "HH:mm:ss"
  weekendsEnabled: boolean;
  publicHolidaysEnabled: boolean;
  publicHolidayRegion: string | null;
}

interface UpsertSchedulingSettingsWire {
  timeZone: string;
  workingHoursEnabled: boolean;
  workingDayStart: string; // "HH:mm"
  workingDayEnd: string;   // "HH:mm"
  weekendsEnabled: boolean;
  publicHolidaysEnabled: boolean;
  publicHolidayRegion: string | null;
}

// ── Mappers ─────────────────────────────────────────────────────

/** "HH:mm:ss" → "HH:mm" */
function trimSeconds(time: string): string {
  const parts = time.split(":");
  return parts.length >= 2 ? `${parts[0]}:${parts[1]}` : time;
}

function mapSettingsFromWire(w: SchedulingSettingsWire): SchedulingSettings {
  return {
    siteId: w.siteId,
    timeZone: w.timeZone,
    workingHoursEnabled: w.workingHoursEnabled,
    workingDayStart: trimSeconds(w.workingDayStart),
    workingDayEnd: trimSeconds(w.workingDayEnd),
    weekendsEnabled: w.weekendsEnabled,
    publicHolidaysEnabled: w.publicHolidaysEnabled,
    publicHolidayRegion: w.publicHolidayRegion,
  };
}

function mapSettingsToWire(s: Omit<SchedulingSettings, "siteId">): UpsertSchedulingSettingsWire {
  return {
    timeZone: s.timeZone,
    workingHoursEnabled: s.workingHoursEnabled,
    workingDayStart: s.workingDayStart,
    workingDayEnd: s.workingDayEnd,
    weekendsEnabled: s.weekendsEnabled,
    publicHolidaysEnabled: s.publicHolidaysEnabled,
    publicHolidayRegion: s.publicHolidayRegion,
  };
}

// ── Settings API ────────────────────────────────────────────────

export async function getSchedulingSettings(siteId: string): Promise<SchedulingSettings> {
  const wire = await apiGet<SchedulingSettingsWire>(API_PATHS.scheduling(siteId));
  return mapSettingsFromWire(wire);
}

export async function upsertSchedulingSettings(
  siteId: string,
  settings: Omit<SchedulingSettings, "siteId">,
): Promise<SchedulingSettings> {
  const wire = await apiPut<SchedulingSettingsWire>(
    API_PATHS.scheduling(siteId),
    mapSettingsToWire(settings),
  );
  return mapSettingsFromWire(wire);
}

export async function deleteSchedulingSettings(siteId: string): Promise<void> {
  return apiDelete(API_PATHS.scheduling(siteId));
}
