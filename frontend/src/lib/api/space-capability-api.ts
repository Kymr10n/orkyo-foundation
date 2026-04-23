import type { CriterionValue } from '@foundation/src/types/criterion';
import { apiGet, apiPost, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export interface SpaceCapability {
  id: string;
  spaceId: string;
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

export async function getSpaceCapabilities(siteId: string, spaceId: string): Promise<SpaceCapability[]> {
  return apiGet<SpaceCapability[]>(API_PATHS.spaceCapabilities(siteId, spaceId));
}

export async function addSpaceCapability(
  siteId: string,
  spaceId: string,
  request: CreateSpaceCapabilityRequest
): Promise<SpaceCapability> {
  return apiPost<SpaceCapability>(
    API_PATHS.spaceCapabilities(siteId, spaceId),
    request
  );
}

export async function deleteSpaceCapability(
  siteId: string,
  spaceId: string,
  capabilityId: string
): Promise<void> {
  return apiDelete(API_PATHS.spaceCapability(siteId, spaceId, capabilityId));
}
