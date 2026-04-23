import type { Criterion, CreateCriterionRequest, UpdateCriterionRequest } from '@foundation/src/types/criterion';
import { apiGet, apiPost, apiPut, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export async function getCriteria(): Promise<Criterion[]> {
  return apiGet<Criterion[]>(API_PATHS.CRITERIA);
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
