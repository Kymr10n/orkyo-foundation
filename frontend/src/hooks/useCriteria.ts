import { createCriterion, deleteCriterion, getCriteria, updateCriterion, updateCriterionApplicability } from "@foundation/src/lib/api/criteria-api";
import type { CreateCriterionRequest, UpdateCriterionRequest, UpdateCriterionApplicabilityRequest, CriterionApplicabilityInfo } from "@foundation/src/types/criterion";
import { useMutation, useQuery } from "@tanstack/react-query";
import { qk } from "@foundation/src/lib/api/query-keys";
import { STALE } from "@foundation/src/lib/core/query-client";

// Criteria drive request requirements; mutating them invalidates the request feed too.
const CRITERIA_INVALIDATES = [qk.criteria.all(), qk.requests.all()] as const;

export const useCriteria = () =>
  useQuery({
    queryKey: qk.criteria.all(),
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
export const useUpdateCriterionApplicability = () =>
  useMutation<CriterionApplicabilityInfo, Error, { id: string; data: UpdateCriterionApplicabilityRequest }>({
    mutationFn: ({ id, data }) => updateCriterionApplicability(id, data),
    // No successMessage: this runs inside EditCriterionDialog's composed save, which
    // already toasts via useUpdateCriterion — a second success toast would double-fire.
    meta: {
      errorMessage: "Failed to update criterion applicability",
      invalidates: [qk.criteria.all()],
    },
  });
