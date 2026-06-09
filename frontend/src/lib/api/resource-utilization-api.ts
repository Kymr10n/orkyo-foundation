/**
 * API client for Resource Utilization queries.
 * Separate from `resources-api.ts` so each per-domain module stays focused —
 * matches the pattern of resource-assignments-api, resource-absences-api,
 * resource-groups-api.
 */

import { apiGet } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export interface ResourceUtilizationBucket {
  start: string;
  end: string;
  allocatedPercent: number;
  effectiveAvailabilityPercent: number;
  isExclusiveOccupied: boolean;
}

export interface ResourceUtilizationResponse {
  from: string;
  to: string;
  granularity: string;
  buckets: ResourceUtilizationBucket[];
}

export async function getResourceUtilization(
  resourceId: string,
  from: Date,
  to: Date,
  granularity: string,
): Promise<ResourceUtilizationResponse> {
  const params = new URLSearchParams({
    from: from.toISOString(),
    to: to.toISOString(),
    granularity,
  });
  return apiGet<ResourceUtilizationResponse>(
    `${API_PATHS.resourceUtilization(resourceId)}?${params}`,
  );
}

/** One resource's utilization buckets, as returned by the bulk endpoint. */
export interface ResourceUtilizationByResource {
  resourceId: string;
  buckets: ResourceUtilizationBucket[];
}

/**
 * Fetch per-resource utilization for every resource of a type in a single
 * request. Replaces the old one-query-per-person fan-out in the People grid.
 */
export async function getUtilizationByResource(
  from: Date,
  to: Date,
  granularity: string,
  resourceTypeKey?: string,
): Promise<ResourceUtilizationByResource[]> {
  const params = new URLSearchParams({
    from: from.toISOString(),
    to: to.toISOString(),
    granularity,
  });
  if (resourceTypeKey) params.set('resourceTypeKey', resourceTypeKey);
  return apiGet<ResourceUtilizationByResource[]>(
    `${API_PATHS.UTILIZATION_BY_RESOURCE}?${params}`,
  );
}
