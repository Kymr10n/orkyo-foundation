import {
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
import { getUtilizationByResource } from "@foundation/src/lib/api/resource-utilization-api";
import {
    getAssignmentsByResourceType,
    validateAssignmentsBatch,
    type ResourceAssignmentInfo,
    type ValidateResourceAssignmentRequest,
} from "@foundation/src/lib/api/resource-assignments-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { errorMessage } from "./mutation-utils";
import { toast } from "sonner";
import { STALE } from "@foundation/src/lib/core/query-client";

// Background refetch cadence for the operational request feeds. Keeps the server-derived status (and
// any worker-sweeper / manual cancel-defer changes) flowing in; the client also recomputes the
// time-derived lifecycle live between fetches (each page applies withEffectiveStatus to its feed).
const REQUESTS_REFETCH_MS = 30_000;

// Canonical placeable-resource hook lives in usePlaceableResources.ts. Re-exported here (not
// redefined) so existing `useUtilization` importers (e.g. UtilizationPage) keep resolving against
// the single source of truth. See F051 dedup.
export { usePlaceableResources } from "@foundation/src/hooks/usePlaceableResources";

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

// ── Resource utilization grid ───────────────────────────────
// The three reads behind one type's grid: occupancy per resource, the assignments in the
// window, and the capability check that decorates their bars.

/** Utilization for every resource of one type in a single request. */
export function useUtilizationByResource(
  resourceTypeKey: string,
  siteId: string | null,
  from: Date,
  to: Date,
  granularity: string,
) {
  return useQuery({
    queryKey: qk.utilization.byResource(resourceTypeKey, siteId ?? null, from, to, granularity),
    queryFn: () => getUtilizationByResource(from, to, granularity, resourceTypeKey, siteId ?? undefined),
    staleTime: STALE.OPERATIONAL,
    placeholderData: (prev) => prev,
  });
}

/** Assignments for every resource of one type in the window — drives the per-segment count badge. */
export function useAssignmentsByType(resourceTypeKey: string, from: Date, to: Date) {
  return useQuery({
    queryKey: qk.utilization.assignmentsByType(resourceTypeKey, from, to),
    queryFn: () => getAssignmentsByResourceType(resourceTypeKey, from, to),
    staleTime: STALE.OPERATIONAL,
  });
}

/**
 * Batch-validate all assignments to surface capability conflicts on bars. Decorative only, so
 * the caller arms it on a delay and the grid renders without waiting for it.
 */
export function useCapabilityConflicts(
  resourceTypeKey: string,
  from: Date,
  to: Date,
  assignments: readonly ResourceAssignmentInfo[],
  enabled: boolean,
) {
  return useQuery({
    // Keyed off the query that produced the assignments plus their count, not the id list
    // itself: React Query hashes the key on every render, and stringifying a thousand uuids
    // per keystroke into the search box is real main-thread time. The ids are a pure function
    // of that query's result, so this identifies the same set.
    queryKey: [
      ...qk.utilization.assignmentsByType(resourceTypeKey, from, to),
      'capability-conflicts',
      assignments.length,
    ],
    queryFn: async (): Promise<Set<string>> => {
      const items: ValidateResourceAssignmentRequest[] = assignments.map((a) => ({
        requestId: a.requestId,
        resourceId: a.resourceId,
        startUtc: a.startUtc,
        endUtc: a.endUtc,
        allocationPercent: a.allocationPercent,
        excludeAssignmentId: a.id,
      }));
      const results = await validateAssignmentsBatch(items);
      const conflicted = new Set<string>();
      for (const item of results) {
        if (item.result.blockers.some((b) => b.code === 'capability.missing')) {
          assignments
            .filter((a) => a.requestId === item.requestId && a.resourceId === item.resourceId)
            .forEach((a) => conflicted.add(a.id));
        }
      }
      return conflicted;
    },
    enabled,
    staleTime: STALE.OPERATIONAL,
  });
}
