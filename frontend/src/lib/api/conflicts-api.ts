/**
 * API client for the tenant-wide conflicts registry (`GET /api/conflicts`).
 *
 * The registry is the authoritative, all-sites/all-dates conflict source for the Conflicts page,
 * the Requests-page badges, and the utilization grid. The grid layers a thin client-side
 * `evaluateSchedule` overlay on top for the bar currently being dragged, so feedback is instant
 * before the mutation commits and this registry refetches.
 */

import type { Conflict } from "@foundation/src/types/requests";
import { apiGet } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

/** All conflicts for one request, as returned by the registry. */
export interface RequestConflicts {
  requestId: string;
  conflicts: Conflict[];
}

export async function getConflicts(): Promise<RequestConflicts[]> {
  return apiGet<RequestConflicts[]>(API_PATHS.CONFLICTS);
}
