import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { getConflicts } from "@foundation/src/lib/api/conflicts-api";
import { getConflictedRequests } from "@foundation/src/lib/api/request-api";
import type { Conflict } from "@foundation/src/types/requests";

/**
 * Tenant-wide conflicts registry — the authoritative, all-sites/all-dates source for the Conflicts
 * page, the Requests-page badges, and the utilization grid. Computed server-side; cached
 * client-side. The grid layers a thin client-side draft overlay on top for the bar being dragged.
 *
 * Conflict mutations should `invalidateQueries({ queryKey: ["conflicts"] })`.
 */
export function useConflictRegistry() {
  const query = useQuery({
    queryKey: ["conflicts"],
    queryFn: getConflicts,
    staleTime: 30_000,
  });

  const conflictsByRequest = useMemo(() => {
    const map = new Map<string, Conflict[]>();
    for (const r of query.data ?? []) map.set(r.requestId, r.conflicts);
    return map;
  }, [query.data]);

  return { ...query, conflictsByRequest };
}

/** The conflicted requests themselves (for the Conflicts page rows / names). */
export function useConflictedRequests() {
  return useQuery({
    queryKey: ["requests", "conflicted"],
    queryFn: () => getConflictedRequests(),
    staleTime: 30_000,
  });
}
