import { apiGet, apiPost, apiPut, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import type { PagedResult } from '../core/paged-result';
import { logger } from '../core/logger';
import type { CustomFieldValue } from './resource-custom-fields-api';
import type { ResourceGeometry } from '../../types/geometry';

/**
 * Directory details — the fields a resource carries when its type declares a directory profile.
 * On the generic contract rather than a person-specific one, and rejected by the backend for any
 * type that has no directory. `null` clears; omitting leaves the stored value alone.
 *
 * Job title and department left this contract in migration 1830, which dropped their columns; both
 * are `list_lookup` custom fields now and ride in `customFields`.
 */
interface ResourceDirectory {
  email?: string | null;
  notes?: string | null;
}

/**
 * Placement — the fields a resource carries when its type declares geometry. Present on the
 * generic contract rather than a space-specific one: a space is just the built-in placeable type,
 * and the backend rejects these fields for any type that cannot be placed.
 */
interface ResourcePlacement {
  /** Short site-unique label shown on the floorplan. */
  code?: string | null;
  /** Whether the resource occupies actual area on the plan (a physical one must have geometry). */
  isPhysical: boolean;
  geometry?: ResourceGeometry | null;
  properties?: Record<string, unknown> | null;
  /** How many concurrent occupants the resource holds. At least 1. */
  capacity: number;
}

export interface ResourceInfo extends ResourcePlacement, ResourceDirectory {
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
  /** Read-only: the group the resource belongs to, when its type is grouped. */
  groupId?: string | null;
  /** Read-only: linking a user is its own operation with its own checks. */
  linkedUserId?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateResourceRequest extends Partial<ResourcePlacement>, ResourceDirectory {
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

/**
 * Every field is optional and absent means "leave alone" — the backend only writes what the
 * request names. `isPhysical` is deliberately absent: a resource cannot stop being physical, so
 * geometry can never be orphaned by an update.
 */
export interface UpdateResourceRequest
  extends Partial<Omit<ResourcePlacement, 'isPhysical'>>, ResourceDirectory {
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

export interface ResourceListFilter {
  resourceTypeKey?: string;
  isActive?: boolean;
  search?: string;
  /** Restricts to resources belonging to this site: its home site, or (people/tools) where they are now. */
  siteId?: string;
  /** Restricts to types that do (or do not) declare geometry — the resources a floorplan holds. */
  hasGeometry?: boolean;
  page?: number;
  pageSize?: number;
}

/**
 * Get resources, optionally filtered. Without `page`/`pageSize` the backend answers with the
 * whole list up to its cap and `hasNextPage` says whether it cut.
 */
export async function getResources(
  filter?: ResourceListFilter,
): Promise<PagedResult<ResourceInfo>> {
  const params = {
    resourceTypeKey: filter?.resourceTypeKey || undefined,
    isActive: filter?.isActive,
    search: filter?.search || undefined,
    siteId: filter?.siteId || undefined,
    hasGeometry: filter?.hasGeometry,
    page: filter?.page || undefined,
    pageSize: filter?.pageSize || undefined,
  };
  const result = await apiGet<PagedResult<ResourceInfo>>(API_PATHS.RESOURCES, { params });
  // An unpaged call gets the whole list up to the backend's cap. `hasNextPage` then means rows
  // were cut off; every picker built on this call would silently miss them, so say so once here.
  if (!filter?.page && result.hasNextPage) {
    logger.warn(
      `getResources: list truncated at ${result.items.length} of ${result.totalItems} resources`,
      params,
    );
  }
  return result;
}

export async function getResource(id: string): Promise<ResourceInfo> {
  return apiGet<ResourceInfo>(API_PATHS.resource(id));
}

export async function createResource(request: CreateResourceRequest): Promise<ResourceInfo> {
  return apiPost<ResourceInfo>(API_PATHS.RESOURCES, request);
}

export async function updateResource(id: string, request: UpdateResourceRequest): Promise<ResourceInfo> {
  return apiPut<ResourceInfo>(API_PATHS.resource(id), request);
}

/**
 * Deactivate a resource
 */
export async function deleteResource(id: string): Promise<void> {
  return apiDelete(API_PATHS.resource(id));
}
