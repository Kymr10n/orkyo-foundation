import type { Request } from "@foundation/src/types/requests";
import type { Space } from "@foundation/src/types/space";
import { durationToMs } from "@foundation/src/domain/scheduling/schedule-model";
import { apiGet, apiPatch } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

// Get all requests
export async function fetchRequests(): Promise<Request[]> {
  const data = await apiGet<Request[]>(API_PATHS.REQUESTS);
  return data.map((r: Request) => ({
    ...r,
    durationMin: durationToMs(r.minimalDurationValue, r.minimalDurationUnit) / 60_000,
  }));
}

// Get all spaces for a site
export async function fetchSpaces(siteId: string): Promise<Space[]> {
  return apiGet<Space[]>(API_PATHS.spaces(siteId));
}

// Update request scheduling
export interface ScheduleRequestData {
  spaceId?: string | null;
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
