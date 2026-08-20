import {
    getAllRequests,
    getScheduledRequests,
    getBacklogRequests,
    scheduleRequest,
    type ScheduleRequestData,
} from "@foundation/src/lib/api/utilization-api";
import {
    applyPlacementAssignmentOptimistic,
    clearPlacementAssignmentOptimistic,
    getPlacementAssignment,
} from "@foundation/src/domain/scheduling/request-assignments";
import { usePlaceableTypeKeys } from "@foundation/src/hooks/usePlaceableResources";
import type { Request } from "@foundation/src/types/requests";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { invalidateRequestData } from "@foundation/src/lib/core/invalidate-request-data";
import { qk } from "@foundation/src/lib/api/query-keys";
import { errorMessage } from "./mutation-utils";
import { toast } from "sonner";

// Background refetch cadence for the operational request feeds. Keeps the server-derived status (and
// any worker-sweeper / manual cancel-defer changes) flowing in; the client also recomputes the
// time-derived lifecycle live between fetches (each page applies withEffectiveStatus to its feed).
const REQUESTS_REFETCH_MS = 30_000;

// Canonical placeable-resource hook lives in usePlaceableResources.ts. Re-exported here (not
// redefined) so existing `useUtilization` importers (e.g. UtilizationPage) keep resolving against
// the single source of truth. See F051 dedup.
export { usePlaceableResources } from "@foundation/src/hooks/usePlaceableResources";

// Fetch all requests (tenant-wide). Kept for non-grid callers; the utilization grid uses the
// scoped hooks below so it never pulls the whole tenant.
// (Standard 5-minute freshness + no focus-refetch are inherited from the global defaults;
// mutations update the cache directly via onSuccess.)
export function useRequests() {
  return useQuery({
    queryKey: qk.requests.all(),
    queryFn: getAllRequests,
    refetchInterval: REQUESTS_REFETCH_MS,
  });
}

// Scheduled requests for the selected site within a buffered window — the grid's bar feed.
export function useScheduledRequests(siteId: string | null, from: Date, to: Date) {
  return useQuery({
    queryKey: qk.requests.scheduled(siteId, from, to),
    queryFn: () => getScheduledRequests(siteId!, from, to),
    enabled: !!siteId,
    refetchInterval: REQUESTS_REFETCH_MS,
  });
}

// Unscheduled backlog (tenant-wide) — drag-to-schedule source for the panel.
export function useBacklogRequests() {
  return useQuery({
    queryKey: qk.requests.backlog(),
    queryFn: getBacklogRequests,
    refetchInterval: REQUESTS_REFETCH_MS,
  });
}

// Mutation: Schedule/unschedule request
export function useScheduleRequest() {
  const queryClient = useQueryClient();
  const placeableKeys = usePlaceableTypeKeys();

  return useMutation({
    // resourceTypeKey is client-side only — it never reaches the API, which resolves the type from
    // the resource id. The optimistic assignment needs it so the synthetic entry carries the same
    // type key the server will write back, otherwise the bar would jump on the next refetch.
    mutationFn: ({
      requestId,
      data,
    }: {
      requestId: string;
      data: ScheduleRequestData;
      resourceTypeKey?: string;
    }) => scheduleRequest(requestId, data),

    // Optimistically update every cached scheduled-window so the bar moves immediately on
    // release. Requests now live under scoped keys (["requests","scheduled",site,from,to]), so we
    // update them all via setQueriesData rather than a single ["requests"] cache.
    onMutate: async ({ requestId, data, resourceTypeKey }) => {
      // The type the optimistic assignment goes under: the one the caller named, else the one the
      // request already sits on. Null when neither is known — the placeable types have not loaded
      // yet — and the optimistic write is skipped rather than invented. An assignment written
      // under an empty key matches no placeable filter, so nothing afterwards could clear it; the
      // server response is what corrects the bar in that case.
      const optimisticTypeKey = (r: Request) =>
        resourceTypeKey ?? getPlacementAssignment(r, placeableKeys)?.resourceTypeKey ?? null;

      await queryClient.cancelQueries({ queryKey: qk.requests.all() });
      const previous = queryClient.getQueriesData<Request[]>({ queryKey: qk.requests.scheduledAll() });

      queryClient.setQueriesData<Request[]>({ queryKey: qk.requests.scheduledAll() }, (old) =>
        old?.map((r) =>
          r.id === requestId
            ? (data.resourceId && data.startTs && data.endTs && optimisticTypeKey(r)
                // A resize keeps the resource it is already on, so its type comes from the
                // existing assignment when the caller did not name one.
                ? applyPlacementAssignmentOptimistic(
                    r,
                    data.resourceId,
                    optimisticTypeKey(r)!,
                    data.startTs,
                    data.endTs,
                    placeableKeys,
                  )
                : (data.resourceId === null
                    ? clearPlacementAssignmentOptimistic(r, placeableKeys)
                    : { ...r, startTs: data.startTs ?? r.startTs, endTs: data.endTs ?? r.endTs }))
            : r
        ) ?? old
      );

      return { previous };
    },

    // Merge the server-confirmed values into the cached entries (spread `r` first so fields not
    // returned by the schedule endpoint — e.g. requirements — survive).
    onSuccess: (updatedRequest) => {
      queryClient.setQueriesData<Request[]>({ queryKey: qk.requests.scheduledAll() }, (old) =>
        old?.map((r) => (r.id === updatedRequest.id ? { ...r, ...updatedRequest } : r)) ?? old
      );
    },

    onError: (err, _vars, context) => {
      // Roll back every snapshotted scheduled-window cache.
      for (const [key, snapshot] of context?.previous ?? []) {
        queryClient.setQueryData(key, snapshot);
      }
      // eslint-disable-next-line no-restricted-syntax -- optimistic-rollback mutation: meta can't express onMutate rollback, feedback stays hand-rolled (docs/dialog-feedback.md)
      toast.error("Failed to schedule request", {
        description: errorMessage(err),
      });
    },

    // Always sync after settling: a schedule/unschedule moves a request between the scoped
    // scheduled windows and the backlog, and changes conflicts — refresh both (prefix match
    // covers every ["requests",…] key).
    onSettled: () => invalidateRequestData(queryClient),
  });
}
