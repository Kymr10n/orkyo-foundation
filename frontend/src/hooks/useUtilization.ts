import {
    fetchRequests,
    fetchSpaces,
    scheduleRequest,
    type ScheduleRequestData,
} from "@/lib/api/utilization-api";
import type { Request } from "@/types/requests";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

// Fetch all requests
export function useRequests() {
  return useQuery({
    queryKey: ["requests"],
    queryFn: fetchRequests,
    staleTime: 5 * 60 * 1000, // 5 minutes — mutations update cache directly via onSuccess
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

    // Optimistically update the cache so the bar moves immediately on release.
    onMutate: async ({ requestId, data }) => {
      // Snapshot first, then apply optimistic update synchronously (within the
      // microtask) so the very next render already has new values.
      const previous = queryClient.getQueryData<Request[]>(["requests"]);

      queryClient.setQueryData<Request[]>(["requests"], (old) =>
        old?.map((r) =>
          r.id === requestId
            ? {
                ...r,
                spaceId: data.spaceId !== undefined ? data.spaceId : r.spaceId,
                startTs: data.startTs !== undefined ? data.startTs : r.startTs,
                endTs: data.endTs !== undefined ? data.endTs : r.endTs,
              }
            : r
        ) ?? []
      );

      // Cancel in-flight refetches AFTER the optimistic write so no stale GET
      // can overwrite it. cancelQueries is awaited to guarantee completion.
      await queryClient.cancelQueries({ queryKey: ["requests"] });

      return { previous };
    },

    // Merge the server-confirmed values into the cached entry.
    // We spread existing `r` first so fields not returned by the schedule
    // endpoint (e.g. requirements) are preserved, then overlay the server
    // response so scheduling fields (startTs, endTs, actualDuration, …) are
    // authoritative. This avoids a follow-up GET that could race and bring
    // back stale timestamps before the DB write is visible.
    onSuccess: (updatedRequest) => {
      queryClient.setQueryData<Request[]>(["requests"], (old) =>
        old?.map((r) =>
          r.id === updatedRequest.id ? { ...r, ...updatedRequest } : r
        ) ?? []
      );
    },

    onError: (_err, _vars, context) => {
      // Roll back to the snapshot if the mutation fails
      if (context?.previous) {
        queryClient.setQueryData(["requests"], context.previous);
      }
    },

    onSettled: (_data, error) => {
      // On success the cache is already authoritative from onSuccess — no
      // immediate refetch needed. On failure, sync to confirm the rollback.
      if (error) {
        queryClient.invalidateQueries({ queryKey: ["requests"] });
      }
    },
  });
}
