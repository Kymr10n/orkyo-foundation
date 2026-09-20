import { createCriterion, deleteCriterion, getCriteria } from "@foundation/src/lib/api/criteria-api";
import type { CreateCriterionRequest } from "@foundation/src/types/criterion";
import { useCallback } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";

// Criteria drive request requirements; mutating them invalidates the request feed too.
// Exported for CriterionEditDialog, which composes its own multi-call save through
// useEntityFormDialog and needs the same invalidation set.
export const CRITERIA_INVALIDATES = [qk.criteria.all(), qk.requests.all()] as const;

/**
 * The criterion catalog.
 *
 * `enabled` lets a dialog fetch only while it is open, on the same key and the same freshness
 * tier as the eager callers, so the dialog shares their cache. Two near-copies of this hook
 * existed for exactly that — one of them omitted the freshness tier and so disagreed with the
 * others about staleness on a key all three shared.
 */
export const useCriteria = (enabled = true) =>
  useQuery({
    queryKey: qk.criteria.all(),
    queryFn: () => getCriteria(),
    staleTime: STALE.OPERATIONAL,
    enabled,
  });

/**
 * The criteria applicable to one resource type. Loaded only while the editor that assigns them
 * is open, so opening a resource list costs nothing.
 */
export const useCriteriaForResourceType = (resourceType: string, enabled: boolean) =>
  useQuery({
    queryKey: qk.criteria.byResourceType(resourceType),
    queryFn: () => getCriteria({ resourceType }),
    enabled,
  });

/** Refresh that list after a criterion was created for the type from inside the editor. */
export const useInvalidateCriteriaForResourceType = (resourceType: string): (() => Promise<void>) => {
  const queryClient = useQueryClient();
  return useCallback(
    () => queryClient.invalidateQueries({ queryKey: qk.criteria.byResourceType(resourceType) }),
    [queryClient, resourceType],
  );
};

export const useCreateCriterion = () =>
  useMutation({
    mutationFn: (data: CreateCriterionRequest) => createCriterion(data),
    meta: {
      successMessage: "Criterion created",
      errorMessage: "Failed to create criterion",
      invalidates: CRITERIA_INVALIDATES,
    },
  });

export const useDeleteCriterion = () =>
  useMutation({
    mutationFn: (id: string) => deleteCriterion(id),
    meta: {
      successMessage: "Criterion deleted",
      errorMessage: "Failed to delete criterion",
      invalidates: CRITERIA_INVALIDATES,
    },
  });
