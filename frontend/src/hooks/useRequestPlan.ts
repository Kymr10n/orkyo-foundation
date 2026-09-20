import { useMutation, useQuery } from "@tanstack/react-query";
import { getRequestPlan, getSitePlan } from "@foundation/src/lib/api/request-plan-api";
import { createChildRequest } from "@foundation/src/lib/api/request-api";
import {
  addRequestDependency,
  deleteRequestDependency,
  type RequestDependency,
} from "@foundation/src/lib/api/request-dependency-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { REQUEST_DERIVED_QUERY_KEYS } from "@foundation/src/lib/core/invalidate-request-data";
import { STALE } from "@foundation/src/lib/core/query-client";
import type { Request } from "@foundation/src/types/requests";

/** One parent's children and the dependencies among them. */
export function useRequestPlan(requestId: string) {
  return useQuery({
    queryKey: qk.requests.plan(requestId),
    queryFn: () => getRequestPlan(requestId),
    staleTime: STALE.OPERATIONAL,
  });
}

/** The plan across a whole site: every leaf task, its group, and every edge among them. */
export function useSitePlan(siteId: string | null) {
  return useQuery({
    queryKey: qk.requests.sitePlan(siteId),
    queryFn: () => getSitePlan(siteId),
    staleTime: STALE.OPERATIONAL,
  });
}

/** Sequence two tasks: `from` runs before `to`. */
export function useLinkPlanDependency(onLinked: (pair: { from: string; to: string }) => void) {
  return useMutation({
    mutationFn: ({ from, to }: { from: string; to: string }) => addRequestDependency(to, from),
    meta: {
      successMessage: "Dependency added",
      errorMessage: "Could not add the dependency",
      invalidates: REQUEST_DERIVED_QUERY_KEYS,
    },
    onSuccess: (_result, variables) => onLinked(variables),
  });
}

/** Create a task directly on the canvas, under the plan's parent. */
export function useAddPlanTask(
  requestId: string,
  nextSortOrder: number,
  onAdded: (created: Request) => void,
) {
  return useMutation({
    mutationFn: (name: string) => createChildRequest(requestId, name, nextSortOrder),
    meta: {
      successMessage: "Task added",
      errorMessage: "Could not add the task",
      invalidates: REQUEST_DERIVED_QUERY_KEYS,
    },
    onSuccess: (created) => onAdded(created),
  });
}

/**
 * Remove one edge. `edges` is the plan's current edge list — the plan can refetch between
 * selecting an edge and confirming its removal, because any request mutation invalidates it.
 * An edge that is already gone is the outcome the user asked for, not a failure to report.
 */
export function useRemovePlanDependency(
  edges: RequestDependency[] | undefined,
  onRemoved: () => void,
) {
  return useMutation({
    mutationFn: (edgeId: string) => {
      const edge = edges?.find((e) => e.id === edgeId);
      if (!edge) return Promise.resolve();
      return deleteRequestDependency(edge.successorRequestId, edgeId);
    },
    meta: {
      successMessage: "Dependency removed",
      errorMessage: "Could not remove the dependency",
      invalidates: REQUEST_DERIVED_QUERY_KEYS,
    },
    onSuccess: () => onRemoved(),
  });
}
