import { useMemo } from "react";
import { useQuery } from "@tanstack/react-query";
import { getConflicts } from "@foundation/src/lib/api/conflicts-api";
import { getConflictedRequests } from "@foundation/src/lib/api/request-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";
import type { Conflict } from "@foundation/src/types/requests";

/**
 * Tenant-wide conflicts registry — the authoritative, all-sites/all-dates source for the Conflicts
 * page, the Requests-page badges, and the utilization grid. Computed server-side; cached
 * client-side. The grid layers a thin client-side draft overlay on top for the bar being dragged.
 *
 * Conflict mutations should `invalidateQueries({ queryKey: qk.conflicts.all() })`.
 */
export interface UseConflictRegistryOptions {
  /** Scope the registry to scheduled bars overlapping this window (the utilization grid passes its
   *  visible range). Omit both for the authoritative all-time view (Conflicts page / Requests badges). */
  from?: Date;
  to?: Date;
  /** Skip the query entirely — e.g. the People tab, which computes its own windowed conflicts. */
  enabled?: boolean;
}

export function useConflictRegistry(options?: UseConflictRegistryOptions) {
  const { from, to, enabled = true } = options ?? {};
  const windowed = from !== undefined && to !== undefined;
  const query = useQuery({
    // qk.conflicts.window keeps "conflicts" as the prefix so mutation invalidations
    // (qk.conflicts.all()) still match every windowed/all-time variant.
    queryKey: qk.conflicts.window(from, to),
    queryFn: () => getConflicts(windowed ? { from, to } : undefined),
    staleTime: STALE.REALTIME,
    enabled,
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
    queryKey: qk.requests.conflicted(),
    queryFn: () => getConflictedRequests(),
    staleTime: STALE.REALTIME,
  });
}
