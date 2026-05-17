import type { Criterion, CreateCriterionRequest, UpdateCriterionRequest } from '@foundation/src/types/criterion';
import { apiGet, apiPost, apiPut, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export interface GetCriteriaParams {
  /** Filter to criteria applicable to a given resource type key (e.g. 'person', 'space'). */
  resourceType?: string;
}

export async function getCriteria(params?: GetCriteriaParams): Promise<Criterion[]> {
  const query = params?.resourceType ? `?resourceType=${encodeURIComponent(params.resourceType)}` : '';
  return apiGet<Criterion[]>(`${API_PATHS.CRITERIA}${query}`);
}

export async function createCriterion(request: CreateCriterionRequest): Promise<Criterion> {
  return apiPost<Criterion>(API_PATHS.CRITERIA, request);
}

export async function updateCriterion(id: string, request: UpdateCriterionRequest): Promise<Criterion> {
  return apiPut<Criterion>(API_PATHS.criterion(id), request);
}

export async function deleteCriterion(id: string): Promise<void> {
  return apiDelete(API_PATHS.criterion(id));
}
