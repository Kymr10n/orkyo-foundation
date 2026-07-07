import type { Criterion, CreateCriterionRequest, UpdateCriterionRequest, CriterionApplicabilityInfo, UpdateCriterionApplicabilityRequest } from '@foundation/src/types/criterion';
import { apiPut } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import { createCrudApi } from './create-crud-api';

export interface GetCriteriaParams {
  /** Filter to criteria applicable to a given resource type key (e.g. 'person', 'space'). */
  resourceType?: string;
}

const criteriaApi = createCrudApi<Criterion, CreateCriterionRequest, UpdateCriterionRequest>({
  collectionPath: API_PATHS.CRITERIA,
  itemPath: API_PATHS.criterion,
});

export async function getCriteria(params?: GetCriteriaParams): Promise<Criterion[]> {
  return criteriaApi.list(
    params?.resourceType ? { resourceType: encodeURIComponent(params.resourceType) } : undefined,
  );
}

export async function createCriterion(request: CreateCriterionRequest): Promise<Criterion> {
  return criteriaApi.create(request);
}

export async function updateCriterion(id: string, request: UpdateCriterionRequest): Promise<Criterion> {
  return criteriaApi.update(id, request);
}

export async function deleteCriterion(id: string): Promise<void> {
  return criteriaApi.remove(id);
}

export async function updateCriterionApplicability(
  id: string,
  request: UpdateCriterionApplicabilityRequest,
): Promise<CriterionApplicabilityInfo> {
  return apiPut<CriterionApplicabilityInfo>(API_PATHS.criterionApplicability(id), request);
}
