import type { QueryClient } from "@tanstack/react-query";
import { qk } from "@foundation/src/lib/api/query-keys";

/**
 * The complete set of query namespaces whose data derives from request + assignment state.
 *
 * CONTRACT: any query that reads request/assignment/scheduling-derived data MUST live under one of
 * these prefixes so a request mutation refreshes it. Master-data namespaces (spaces, scheduling,
 * departments, tenant-settings, …) MUST NOT be added — they don't change when a request does.
 *
 * - requests / conflicts: the request lists/feeds and the tenant-wide conflicts registry. Conflicts
 *   are derived from request state (window vs minimal duration, assignments, …).
 * - utilization-by-resource / resource-assignments-by-type: the people/space occupancy grids.
 * - insights: the overview/utilization/conflicts/requests trend charts (all request-derived).
 *
 * Each entry is a prefix: `["requests"]` covers `["requests","scheduled",…]`, backlog, etc.
 */
export const REQUEST_DERIVED_QUERY_KEYS = [
  qk.requests.all(),
  qk.conflicts.all(),
  qk.utilization.byResourceAll(),
  qk.utilization.assignmentsByTypeAll(),
  qk.insights.all(),
] as const;

/**
 * Invalidate everything a request (or its assignments) can change. Every request/assignment mutation
 * routes through this single chokepoint — otherwise the request lists, conflict badges, occupancy
 * grids, or insights charts go stale. Broad prefix invalidation is cheap because React Query only
 * refetches active queries (inactive ones are marked stale and refetch on next mount).
 */
export function invalidateRequestData(queryClient: QueryClient): void {
  for (const queryKey of REQUEST_DERIVED_QUERY_KEYS) {
    queryClient.invalidateQueries({ queryKey });
  }
}
