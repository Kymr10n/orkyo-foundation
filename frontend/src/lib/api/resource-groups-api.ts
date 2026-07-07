/**
 * API client for resource group CRUD operations.
 * Used by the People → Groups tab to manage people groups.
 */

import { apiGet, apiPut } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import type { ResourceInfo } from './resources-api';
import { createCrudApi } from './create-crud-api';

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

interface CreateResourceGroupRequest {
  resourceTypeKey: string;
  name: string;
  description?: string;
  defaultAvailabilityPercent: number;
  color?: string;
  displayOrder?: number;
}

interface UpdateResourceGroupRequest {
  name?: string;
  description?: string;
  defaultAvailabilityPercent?: number;
  color?: string;
  displayOrder?: number;
}

const resourceGroupsApi = createCrudApi<ResourceGroupInfo, CreateResourceGroupRequest, UpdateResourceGroupRequest>({
  collectionPath: API_PATHS.RESOURCE_GROUPS,
  itemPath: API_PATHS.resourceGroup,
});

export async function getResourceGroups(resourceTypeKey: string): Promise<ResourceGroupInfo[]> {
  return resourceGroupsApi.list({ resourceTypeKey: encodeURIComponent(resourceTypeKey) });
}

export async function createResourceGroup(request: CreateResourceGroupRequest): Promise<ResourceGroupInfo> {
  return resourceGroupsApi.create(request);
}

export async function updateResourceGroup(
  id: string,
  request: UpdateResourceGroupRequest,
): Promise<ResourceGroupInfo> {
  return resourceGroupsApi.update(id, request);
}

export async function deleteResourceGroup(id: string): Promise<void> {
  return resourceGroupsApi.remove(id);
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
