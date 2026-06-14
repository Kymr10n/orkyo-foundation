import {
    fetchRequests,
    fetchScheduledRequests,
    fetchBacklogRequests,
    fetchSpaces,
    scheduleRequest,
    type ScheduleRequestData,
} from "@foundation/src/lib/api/utilization-api";
import { applySpaceAssignmentOptimistic, clearSpaceAssignmentOptimistic } from "@foundation/src/domain/scheduling/request-assignments";
import type { Request } from "@foundation/src/types/requests";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { invalidateRequestData } from "@foundation/src/lib/core/invalidate-request-data";
import { toast } from "sonner";

// Fetch all requests (tenant-wide). Kept for non-grid callers; the utilization grid uses the
// scoped hooks below so it never pulls the whole tenant.
export function useRequests() {
  return useQuery({
    queryKey: ["requests"],
    queryFn: fetchRequests,
    staleTime: 5 * 60 * 1000, // 5 minutes — mutations update cache directly via onSuccess
    refetchOnWindowFocus: false,
  });
}

// Scheduled requests for the selected site within a buffered window — the grid's bar feed.
export function useScheduledRequests(siteId: string | null, from: Date, to: Date) {
  return useQuery({
    queryKey: ["requests", "scheduled", siteId, from.toISOString(), to.toISOString()],
    queryFn: () => fetchScheduledRequests(siteId!, from, to),
    enabled: !!siteId,
    staleTime: 5 * 60 * 1000,
    refetchOnWindowFocus: false,
  });
}

// Unscheduled backlog (tenant-wide) — drag-to-schedule source for the panel.
export function useBacklogRequests() {
  return useQuery({
    queryKey: ["requests", "backlog"],
    queryFn: fetchBacklogRequests,
    staleTime: 5 * 60 * 1000,
    refetchOnWindowFocus: false,
  });
}

// Fetch all spaces for selected site
export function useSpaces(siteId: string | null) {
  return useQuery({
    queryKey: ["spaces", siteId],
    queryFn: () => fetchSpaces(siteId!),
    enabled: !!siteId,
    staleTime: 5 * 60 * 1000, // Spaces change infrequently
    refetchOnWindowFocus: false,
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
      await queryClient.cancelQueries({ queryKey: ["requests"] });
      const previous = queryClient.getQueriesData<Request[]>({ queryKey: ["requests", "scheduled"] });

      queryClient.setQueriesData<Request[]>({ queryKey: ["requests", "scheduled"] }, (old) =>
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
      queryClient.setQueriesData<Request[]>({ queryKey: ["requests", "scheduled"] }, (old) =>
        old?.map((r) => (r.id === updatedRequest.id ? { ...r, ...updatedRequest } : r)) ?? old
      );
    },

    onError: (err, _vars, context) => {
      // Roll back every snapshotted scheduled-window cache.
      for (const [key, snapshot] of context?.previous ?? []) {
        queryClient.setQueryData(key, snapshot);
      }
      toast.error("Failed to schedule request", {
        description: err instanceof Error ? err.message : String(err),
      });
    },

    // Always sync after settling: a schedule/unschedule moves a request between the scoped
    // scheduled windows and the backlog, and changes conflicts — refresh both (prefix match
    // covers every ["requests",…] key).
    onSettled: () => invalidateRequestData(queryClient),
  });
}
