import {
    fetchRequests,
    fetchScheduledRequests,
    fetchBacklogRequests,
    scheduleRequest,
    type ScheduleRequestData,
} from "@foundation/src/lib/api/utilization-api";
import { applySpaceAssignmentOptimistic, clearSpaceAssignmentOptimistic } from "@foundation/src/domain/scheduling/request-assignments";
import type { Request } from "@foundation/src/types/requests";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { invalidateRequestData } from "@foundation/src/lib/core/invalidate-request-data";
import { qk } from "@foundation/src/lib/api/query-keys";
import { errorMessage } from "./mutation-utils";
import { toast } from "sonner";

// Canonical spaces hook lives in useSpaces.ts. Re-exported here (not redefined) so
// existing `useUtilization` importers (e.g. UtilizationPage) keep resolving against
// the single source of truth. See F051 dedup.
export { useSpaces } from "@foundation/src/hooks/useSpaces";

// Fetch all requests (tenant-wide). Kept for non-grid callers; the utilization grid uses the
// scoped hooks below so it never pulls the whole tenant.
// (Standard 5-minute freshness + no focus-refetch are inherited from the global defaults;
// mutations update the cache directly via onSuccess.)
export function useRequests() {
  return useQuery({
    queryKey: qk.requests.all(),
    queryFn: fetchRequests,
  });
}

// Scheduled requests for the selected site within a buffered window — the grid's bar feed.
export function useScheduledRequests(siteId: string | null, from: Date, to: Date) {
  return useQuery({
    queryKey: qk.requests.scheduled(siteId, from, to),
    queryFn: () => fetchScheduledRequests(siteId!, from, to),
    enabled: !!siteId,
  });
}

// Unscheduled backlog (tenant-wide) — drag-to-schedule source for the panel.
export function useBacklogRequests() {
  return useQuery({
    queryKey: qk.requests.backlog(),
    queryFn: fetchBacklogRequests,
  });
}

// Mutation: Schedule/unschedule request
export function useScheduleRequest() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      requestId,
      data,
    }: {
      requestId: string;
      data: ScheduleRequestData;
    }) => scheduleRequest(requestId, data),

    // Optimistically update every cached scheduled-window so the bar moves immediately on
    // release. Requests now live under scoped keys (["requests","scheduled",site,from,to]), so we
    // update them all via setQueriesData rather than a single ["requests"] cache.
    onMutate: async ({ requestId, data }) => {
      await queryClient.cancelQueries({ queryKey: qk.requests.all() });
      const previous = queryClient.getQueriesData<Request[]>({ queryKey: qk.requests.scheduledAll() });

      queryClient.setQueriesData<Request[]>({ queryKey: qk.requests.scheduledAll() }, (old) =>
        old?.map((r) =>
          r.id === requestId
            ? (data.resourceId && data.startTs && data.endTs
                ? applySpaceAssignmentOptimistic(r, data.resourceId, data.startTs, data.endTs)
                : (data.resourceId === null
                    ? clearSpaceAssignmentOptimistic(r)
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
