import { createCrudHooks } from "./useMutations";
import { createCriterion, deleteCriterion, getCriteria, updateCriterion } from "@foundation/src/lib/api/criteria-api";
import type { Criterion, CreateCriterionRequest, UpdateCriterionRequest } from "@foundation/src/types/criterion";

const CRITERIA_QUERY_KEY = ["criteria"] as const;

const criteriaHooks = createCrudHooks<Criterion, CreateCriterionRequest, UpdateCriterionRequest>({
  queryKey: () => CRITERIA_QUERY_KEY,
  queryFn: () => getCriteria(),
  createFn: (data) => createCriterion(data),
  updateFn: (id, data) => updateCriterion(id, data),
  deleteFn: (id) => deleteCriterion(id),
  invalidateKeys: () => [["requests"]],
});

export const useCriteria = () => criteriaHooks.useQuery(undefined, { enabled: true });
export const useCreateCriterion = () => criteriaHooks.useCreate();
export const useUpdateCriterion = () => criteriaHooks.useUpdate();
export const useDeleteCriterion = () => criteriaHooks.useDelete();
