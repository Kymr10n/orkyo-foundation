/**
 * API client for Resource CRUD operations
 */

import { apiGet, apiPost, apiPut, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export interface ResourceInfo {
  id: string;
  resourceTypeId: string;
  resourceTypeKey: string;
  name: string;
  description?: string;
  externalReference?: string;
  allocationMode: string;
  baseAvailabilityPercent: number;
  isActive: boolean;
  /** Administrative/owning site (null for spaces and un-remediated resources). */
  homeSiteId?: string | null;
  /** Site where the resource is currently available (defaults to home site). */
  currentSiteId?: string | null;
  /** Whether the resource may be assigned to requests at another site (backend defaults true). */
  crossSiteAllowed?: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateResourceRequest {
  resourceTypeKey: string;
  name: string;
  description?: string;
  externalReference?: string;
  allocationMode: string;
  baseAvailabilityPercent?: number;
  homeSiteId?: string | null;
  currentSiteId?: string | null;
  crossSiteAllowed?: boolean;
}

export interface UpdateResourceRequest {
  name?: string;
  description?: string;
  externalReference?: string;
  allocationMode?: string;
  baseAvailabilityPercent?: number;
  isActive?: boolean;
  homeSiteId?: string | null;
  currentSiteId?: string | null;
  crossSiteAllowed?: boolean;
}

export interface ResourcesResponse {
  data: ResourceInfo[];
  total: number;
  page: number;
  pageSize: number;
}

export interface ResourceListFilter {
  resourceTypeKey?: string;
  isActive?: boolean;
  search?: string;
  page?: number;
  pageSize?: number;
}

/**
 * Get all resources, optionally filtered
 */
export async function getResources(filter?: ResourceListFilter): Promise<ResourcesResponse> {
  const params = new URLSearchParams();
  if (filter?.resourceTypeKey) params.append('resourceTypeKey', filter.resourceTypeKey);
  if (filter?.isActive !== undefined) params.append('isActive', String(filter.isActive));
  if (filter?.search) params.append('search', filter.search);
  if (filter?.page) params.append('page', String(filter.page));
  if (filter?.pageSize) params.append('pageSize', String(filter.pageSize));

  const queryString = params.toString();
  return apiGet<ResourcesResponse>(`${API_PATHS.RESOURCES}?${queryString}`);
}

/**
 * Get a single resource by ID
 */
export async function getResource(id: string): Promise<ResourceInfo> {
  return apiGet<ResourceInfo>(API_PATHS.resource(id));
}

/**
 * Create a new resource
 */
export async function createResource(request: CreateResourceRequest): Promise<ResourceInfo> {
  return apiPost<ResourceInfo>(API_PATHS.RESOURCES, request);
}

/**
 * Update an existing resource
 */
export async function updateResource(id: string, request: UpdateResourceRequest): Promise<ResourceInfo> {
  return apiPut<ResourceInfo>(API_PATHS.resource(id), request);
}

/**
 * Deactivate a resource
 */
export async function deleteResource(id: string): Promise<void> {
  return apiDelete(API_PATHS.resource(id));
}
