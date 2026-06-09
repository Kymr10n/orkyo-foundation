import type { Request } from "@foundation/src/types/requests";
import type { Space } from "@foundation/src/types/space";
import { durationToMs } from "@foundation/src/domain/scheduling/schedule-model";
import { apiGet, apiPatch } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

const withDurationMin = (r: Request): Request => ({
  ...r,
  durationMin: durationToMs(r.minimalDurationValue, r.minimalDurationUnit) / 60_000,
});

// Get all requests (tenant-wide). Prefer the scoped fetchers below for the utilization grid.
export async function fetchRequests(): Promise<Request[]> {
  return (await apiGet<Request[]>(API_PATHS.REQUESTS)).map(withDurationMin);
}

// Scheduled requests for one site whose bar overlaps [from,to] — the grid's bar feed.
export async function fetchScheduledRequests(siteId: string, from: Date, to: Date): Promise<Request[]> {
  const params = new URLSearchParams({ from: from.toISOString(), to: to.toISOString() });
  return (await apiGet<Request[]>(`${API_PATHS.siteRequests(siteId)}?${params}`)).map(withDurationMin);
}

// Unscheduled backlog (tenant-wide) — drag-to-schedule source for the panel.
export async function fetchBacklogRequests(): Promise<Request[]> {
  return (await apiGet<Request[]>(`${API_PATHS.REQUESTS}?scheduled=false`)).map(withDurationMin);
}

// Get all spaces for a site
export async function fetchSpaces(siteId: string): Promise<Space[]> {
  return apiGet<Space[]>(API_PATHS.spaces(siteId));
}

// Update request scheduling
export interface ScheduleRequestData {
  resourceId?: string | null;
  startTs?: string | null;
  endTs?: string | null;
}

export async function scheduleRequest(
  requestId: string,
  data: ScheduleRequestData
): Promise<Request> {
  const result = await apiPatch<Request>(API_PATHS.requestSchedule(requestId), data);
  return {
    ...result,
    durationMin: durationToMs(result.minimalDurationValue, result.minimalDurationUnit) / 60_000,
  };
}
