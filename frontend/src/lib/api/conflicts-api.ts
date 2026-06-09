/**
 * API client for the tenant-wide conflicts registry (`GET /api/conflicts`).
 *
 * The registry is the authoritative, all-sites/all-dates conflict source for the Conflicts page
 * and the Requests-page badges. The utilization grid computes its own scoped, contextual conflicts
 * separately (client `evaluateSchedule` + `validate-batch` over the visible site/window).
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
