import type { CriterionValue } from '@foundation/src/types/criterion';
import { apiGet, apiPost, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export interface ResourceCapability {
  id: string;
  resourceId: string;
  criterionId: string;
  value: CriterionValue;
  createdAt: string;
  updatedAt: string;
  criterion: {
    id: string;
    name: string;
    dataType: string;
    unit?: string;
  };
}

export interface UpsertResourceCapabilityRequest {
  criterionId: string;
  value: CriterionValue;
}

export async function getResourceCapabilities(resourceId: string): Promise<ResourceCapability[]> {
  return apiGet<ResourceCapability[]>(API_PATHS.resourceCapabilities(resourceId));
}

export async function upsertResourceCapability(
  resourceId: string,
  request: UpsertResourceCapabilityRequest,
): Promise<ResourceCapability> {
  return apiPost<ResourceCapability>(API_PATHS.resourceCapabilities(resourceId), request);
}

export async function deleteResourceCapability(
  resourceId: string,
  capabilityId: string,
): Promise<void> {
  return apiDelete(API_PATHS.resourceCapability(resourceId, capabilityId));
}
