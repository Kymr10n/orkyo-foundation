/**
 * API client for Resource CRUD operations
 */

import { apiGet, apiPost, apiPut, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import type { CustomFieldValue } from './resource-custom-fields-api';

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
  /** Administrative/owning site and idle-time anchor (null for spaces and un-remediated resources). */
  homeSiteId?: string | null;
  /** Derived, read-only: where the resource is right now — the site of the non-cancelled assignment
   * overlapping the current time, else the home site (spaces resolve to their own site). Computed by
   * the backend; not settable. */
  currentSiteId?: string | null;
  /** Whether the resource may be assigned to requests at another site (backend defaults true). */
  crossSiteAllowed?: boolean;
  /** Values for the type's custom fields, keyed by field key. Includes values for retired
   * fields, so an edit that sends the document back keeps them. */
  customFields?: Record<string, CustomFieldValue> | null;
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
  crossSiteAllowed?: boolean;
  /** Values for the type's custom fields. Absent is the same as empty, so a required field
   * cannot be skipped by leaving the document out. */
  customFields?: Record<string, CustomFieldValue>;
}

export interface UpdateResourceRequest {
  name?: string;
  description?: string;
  externalReference?: string;
  allocationMode?: string;
  baseAvailabilityPercent?: number;
  isActive?: boolean;
  homeSiteId?: string | null;
  crossSiteAllowed?: boolean;
  /** Omit to leave stored values untouched; a supplied document replaces them wholesale. */
  customFields?: Record<string, CustomFieldValue>;
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
  /** Restricts to resources belonging to this site: its home site, or (people/tools) where they are now. */
  siteId?: string;
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
  if (filter?.siteId) params.append('siteId', filter.siteId);
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
