import type {
  SchedulingSettings,
  OffTimeDefinition,
  OffTimeType,
} from "@/domain/scheduling/types";
import { apiGet, apiPut, apiPost, apiDelete } from "../core/api-client";
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

interface OffTimeWire {
  id: string;
  siteId: string;
  title: string;
  type: OffTimeType;
  appliesToAllSpaces: boolean;
  spaceIds: string[] | null;
  startTs: string; // ISO 8601
  endTs: string;   // ISO 8601
  isRecurring: boolean;
  recurrenceRule: string | null;
  enabled: boolean;
}

interface CreateOffTimeWire {
  title: string;
  type: OffTimeType;
  appliesToAllSpaces: boolean;
  spaceIds?: string[];
  startTs: string;
  endTs: string;
  isRecurring: boolean;
  recurrenceRule?: string | null;
  enabled: boolean;
}

interface UpdateOffTimeWire {
  title?: string;
  type?: OffTimeType;
  appliesToAllSpaces?: boolean;
  spaceIds?: string[];
  startTs?: string;
  endTs?: string;
  isRecurring?: boolean;
  recurrenceRule?: string | null;
  enabled?: boolean;
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

function mapOffTimeFromWire(w: OffTimeWire): OffTimeDefinition {
  return {
    id: w.id,
    siteId: w.siteId,
    title: w.title,
    type: w.type,
    appliesToAllSpaces: w.appliesToAllSpaces,
    spaceIds: w.spaceIds ?? [],
    startMs: new Date(w.startTs).getTime(),
    endMs: new Date(w.endTs).getTime(),
    isRecurring: w.isRecurring,
    recurrenceRule: w.recurrenceRule,
    enabled: w.enabled,
  };
}

function mapOffTimeToCreateWire(o: Omit<OffTimeDefinition, "id" | "siteId">): CreateOffTimeWire {
  const wire: CreateOffTimeWire = {
    title: o.title,
    type: o.type,
    appliesToAllSpaces: o.appliesToAllSpaces,
    startTs: new Date(o.startMs).toISOString(),
    endTs: new Date(o.endMs).toISOString(),
    isRecurring: o.isRecurring,
    recurrenceRule: o.recurrenceRule,
    enabled: o.enabled,
  };
  if (!o.appliesToAllSpaces) wire.spaceIds = o.spaceIds;
  return wire;
}

function mapOffTimeToUpdateWire(o: Partial<Omit<OffTimeDefinition, "id" | "siteId">>): UpdateOffTimeWire {
  const wire: UpdateOffTimeWire = {};
  if (o.title !== undefined) wire.title = o.title;
  if (o.type !== undefined) wire.type = o.type;
  if (o.appliesToAllSpaces !== undefined) wire.appliesToAllSpaces = o.appliesToAllSpaces;
  if (o.spaceIds !== undefined) wire.spaceIds = o.spaceIds;
  if (o.startMs !== undefined) wire.startTs = new Date(o.startMs).toISOString();
  if (o.endMs !== undefined) wire.endTs = new Date(o.endMs).toISOString();
  if (o.isRecurring !== undefined) wire.isRecurring = o.isRecurring;
  if (o.recurrenceRule !== undefined) wire.recurrenceRule = o.recurrenceRule;
  if (o.enabled !== undefined) wire.enabled = o.enabled;
  return wire;
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

// ── Off-Times API ───────────────────────────────────────────────

export async function getOffTimes(siteId: string): Promise<OffTimeDefinition[]> {
  const wire = await apiGet<OffTimeWire[]>(API_PATHS.offTimes(siteId));
  return wire.map(mapOffTimeFromWire);
}

export async function createOffTime(
  siteId: string,
  offTime: Omit<OffTimeDefinition, "id" | "siteId">,
): Promise<OffTimeDefinition> {
  const wire = await apiPost<OffTimeWire>(
    API_PATHS.offTimes(siteId),
    mapOffTimeToCreateWire(offTime),
  );
  return mapOffTimeFromWire(wire);
}

export async function updateOffTime(
  siteId: string,
  offTimeId: string,
  updates: Partial<Omit<OffTimeDefinition, "id" | "siteId">>,
): Promise<OffTimeDefinition> {
  const wire = await apiPut<OffTimeWire>(
    API_PATHS.offTime(siteId, offTimeId),
    mapOffTimeToUpdateWire(updates),
  );
  return mapOffTimeFromWire(wire);
}

export async function deleteOffTime(siteId: string, offTimeId: string): Promise<void> {
  return apiDelete(API_PATHS.offTime(siteId, offTimeId));
}
