import type { CriterionValue } from '@foundation/src/types/criterion';
import { apiGet, apiPost, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export interface SpaceCapability {
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

interface CreateSpaceCapabilityRequest {
  criterionId: string;
  value: CriterionValue;
}

export async function getSpaceCapabilities(siteId: string, resourceId: string): Promise<SpaceCapability[]> {
  return apiGet<SpaceCapability[]>(API_PATHS.spaceCapabilities(siteId, resourceId));
}

export async function addSpaceCapability(
  siteId: string,
  resourceId: string,
  request: CreateSpaceCapabilityRequest
): Promise<SpaceCapability> {
  return apiPost<SpaceCapability>(
    API_PATHS.spaceCapabilities(siteId, resourceId),
    request
  );
}

export async function deleteSpaceCapability(
  siteId: string,
  resourceId: string,
  capabilityId: string
): Promise<void> {
  return apiDelete(API_PATHS.spaceCapability(siteId, resourceId, capabilityId));
}
