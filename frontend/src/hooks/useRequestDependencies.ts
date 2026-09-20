import { useMutation, useQuery } from "@tanstack/react-query";
import {
  addRequestDependency,
  deleteRequestDependency,
  getRequestDependencies,
} from "@foundation/src/lib/api/request-dependency-api";
import { updateRequest } from "@foundation/src/lib/api/request-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { REQUEST_DERIVED_QUERY_KEYS } from "@foundation/src/lib/core/invalidate-request-data";
import { STALE } from "@foundation/src/lib/core/query-client";
import type { PredecessorLogic } from "@foundation/src/types/requests";

/** What a request waits for, and what waits on it. */
export function useRequestDependencies(requestId: string | undefined) {
  return useQuery({
    queryKey: qk.requests.dependencies(requestId ?? ""),
    queryFn: () => getRequestDependencies(requestId!),
    enabled: !!requestId,
    staleTime: STALE.REALTIME,
  });
}

export function useAddRequestDependency(requestId: string | undefined, onAdded: () => void) {
  return useMutation({
    mutationFn: ({ predecessorId, lagMinutes }: { predecessorId: string; lagMinutes: number }) =>
      addRequestDependency(requestId!, predecessorId, lagMinutes),
    meta: {
      successMessage: "Dependency added",
      errorMessage: "Could not add the dependency",
      invalidates: REQUEST_DERIVED_QUERY_KEYS,
    },
    onSuccess: () => onAdded(),
  });
}

/**
 * The condition is a property of the request, not of an edge, so it rides the request update
 * rather than getting an endpoint of its own. Logic and k always travel together: the server
 * clears k unless the logic is k_of_n, which is what stops a stale k outliving its logic.
 */
export function useUpdateStartCondition(requestId: string | undefined) {
  return useMutation({
    mutationFn: (next: { logic: PredecessorLogic; k: number | null }) =>
      updateRequest(requestId!, {
        predecessorLogic: next.logic,
        predecessorLogicK: next.logic === "k_of_n" ? next.k : null,
      }),
    meta: {
      successMessage: "Start condition updated",
      errorMessage: "Could not update the start condition",
      invalidates: REQUEST_DERIVED_QUERY_KEYS,
    },
  });
}

export function useRemoveRequestDependency(requestId: string | undefined) {
  return useMutation({
    mutationFn: (dependencyId: string) => deleteRequestDependency(requestId!, dependencyId),
    meta: {
      successMessage: "Dependency removed",
      errorMessage: "Could not remove the dependency",
      invalidates: REQUEST_DERIVED_QUERY_KEYS,
    },
  });
}
