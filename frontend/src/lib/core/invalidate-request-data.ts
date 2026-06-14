import type { QueryClient } from "@tanstack/react-query";

/**
 * Invalidate everything a request mutation can change: the request lists/feeds AND the
 * tenant-wide conflicts registry. Conflicts are derived from request state (window vs minimal
 * duration, assignments, …), so any create/update/delete/move/schedule of a request must refresh
 * both — otherwise the grid's conflict badges go stale (see useConflictRegistry).
 *
 * Prefix match on `["requests"]` covers every scoped key (["requests","scheduled",…], backlog, etc.).
 */
export function invalidateRequestData(queryClient: QueryClient): void {
  queryClient.invalidateQueries({ queryKey: ["requests"] });
  queryClient.invalidateQueries({ queryKey: ["conflicts"] });
}
