import { createCrudHooks } from "./useMutations";
import { createCriterion, deleteCriterion, getCriteria, updateCriterion, updateCriterionApplicability } from "@foundation/src/lib/api/criteria-api";
import type { Criterion, CreateCriterionRequest, UpdateCriterionRequest, UpdateCriterionApplicabilityRequest, CriterionApplicabilityInfo, ResourceTypeKey } from "@foundation/src/types/criterion";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

const CRITERIA_QUERY_KEY = ["criteria"] as const;

const criteriaHooks = createCrudHooks<Criterion, CreateCriterionRequest, UpdateCriterionRequest>({
  queryKey: () => CRITERIA_QUERY_KEY,
  queryFn: () => getCriteria(),
  createFn: (data) => createCriterion(data),
  updateFn: (id, data) => updateCriterion(id, data),
  deleteFn: (id) => deleteCriterion(id),
  invalidateKeys: () => [["requests"]],
  entityLabel: "Criterion",
});

export const useCriteria = () => criteriaHooks.useQuery(undefined, { enabled: true });
export const useCreateCriterion = () => criteriaHooks.useCreate();
export const useUpdateCriterion = () => criteriaHooks.useUpdate();
export const useDeleteCriterion = () => criteriaHooks.useDelete();

/** Returns only criteria applicable to the given resource type. */
export function useFilteredCriteria(resourceType: ResourceTypeKey) {
  return useQuery<Criterion[]>({
    queryKey: [...CRITERIA_QUERY_KEY, resourceType],
    queryFn: () => getCriteria({ resourceType }),
  });
}

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
