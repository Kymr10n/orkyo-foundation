import { createCriterion, deleteCriterion, getCriteria, updateCriterion, updateCriterionApplicability } from "@foundation/src/lib/api/criteria-api";
import type { CreateCriterionRequest, UpdateCriterionRequest, UpdateCriterionApplicabilityRequest, CriterionApplicabilityInfo } from "@foundation/src/types/criterion";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";

const CRITERIA_QUERY_KEY = ["criteria"] as const;

// Criteria drive request requirements; mutating them invalidates the request feed too.
const CRITERIA_INVALIDATES = [CRITERIA_QUERY_KEY, qk.requests.all()] as const;

export const useCriteria = () =>
  useQuery({
    queryKey: CRITERIA_QUERY_KEY,
    queryFn: () => getCriteria(),
    staleTime: STALE.OPERATIONAL,
  });

export const useCreateCriterion = () =>
  useMutation({
    mutationFn: (data: CreateCriterionRequest) => createCriterion(data),
    meta: {
      successMessage: "Criterion created",
      errorMessage: "Failed to create criterion",
      invalidates: CRITERIA_INVALIDATES,
    },
  });

export const useUpdateCriterion = () =>
  useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateCriterionRequest }) => updateCriterion(id, data),
    meta: {
      successMessage: "Criterion updated",
      errorMessage: "Failed to update criterion",
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

/** Mutation to update criterion applicability via PUT /criteria/{id}/applicability. */
export function useUpdateCriterionApplicability() {
  const queryClient = useQueryClient();
  return useMutation<CriterionApplicabilityInfo, Error, { id: string; data: UpdateCriterionApplicabilityRequest }>({
    mutationFn: ({ id, data }) => updateCriterionApplicability(id, data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: CRITERIA_QUERY_KEY });
    },
  });
}
