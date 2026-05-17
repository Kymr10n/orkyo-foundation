/**
 * API client for resource group CRUD operations.
 * Used by the People → Groups tab to manage people groups.
 */

import { apiGet, apiPost, apiPut, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import type { ResourceInfo } from './resources-api';

export interface ResourceGroupInfo {
  id: string;
  name: string;
  description?: string;
  defaultAvailabilityPercent: number;
  memberCount: number;
  createdAt: string;
  updatedAt: string;
  resourceTypeKey: string;
  color?: string;
  displayOrder?: number;
}

export async function getResourceGroups(resourceTypeKey: string): Promise<ResourceGroupInfo[]> {
  return apiGet<ResourceGroupInfo[]>(`${API_PATHS.RESOURCE_GROUPS}?resourceTypeKey=${encodeURIComponent(resourceTypeKey)}`);
}

export async function createResourceGroup(request: {
  resourceTypeKey: string;
  name: string;
  description?: string;
  defaultAvailabilityPercent: number;
  color?: string;
  displayOrder?: number;
}): Promise<ResourceGroupInfo> {
  return apiPost<ResourceGroupInfo>(API_PATHS.RESOURCE_GROUPS, request);
}

export async function updateResourceGroup(
  id: string,
  request: { name?: string; description?: string; defaultAvailabilityPercent?: number; color?: string; displayOrder?: number },
): Promise<ResourceGroupInfo> {
  return apiPut<ResourceGroupInfo>(API_PATHS.resourceGroup(id), request);
}

export async function deleteResourceGroup(id: string): Promise<void> {
  return apiDelete(API_PATHS.resourceGroup(id));
}

export interface ResourceGroupMembersResponse {
  groupId: string;
  members: ResourceInfo[];
}

export async function getResourceGroupMembers(id: string): Promise<ResourceGroupMembersResponse> {
  return apiGet<ResourceGroupMembersResponse>(API_PATHS.resourceGroupMembers(id));
}

/** Replace-all semantics — pass the full final membership list. */
export async function setResourceGroupMembers(
  id: string,
  resourceIds: string[],
): Promise<ResourceGroupMembersResponse> {
  return apiPut<ResourceGroupMembersResponse>(API_PATHS.resourceGroupMembers(id), { resourceIds });
}
