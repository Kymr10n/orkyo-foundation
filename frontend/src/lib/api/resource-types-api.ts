/**
 * API client for resource types and their custom field definitions.
 *
 * Resource types are the tenant's catalogue of manageable things. Three are seeded and
 * system-owned (space, person, tool); tenants may define their own (car, camera, …) and
 * give each a set of custom fields whose values live on each resource's `metadata` document.
 */

import { API_PATHS } from '../core/api-paths';
import { createCrudApi } from './create-crud-api';

export interface ResourceTypeInfo {
  id: string;
  key: string;
  displayName: string;
  /** Plural — labels a collection (sidebar entry, utilization tab, page title). */
  displayNamePlural: string;
  description?: string;
  /** lucide-react icon name; unknown or absent names fall back to a default. */
  icon?: string | null;
  /** Placeable on a floorplan: carries a code, geometry, a capacity and an owning site. */
  hasGeometry: boolean;
  /** Carries directory details — email, job title, department, a linked user account. */
  hasDirectoryProfile: boolean;
  /** Belongs to at most one group; enforced in the database. */
  singleGroupMembership: boolean;
  /** Seeded types (space, person, tool) — identity, lifecycle and behaviour are read-only. */
  isSystem: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateResourceTypeRequest {
  key: string;
  displayName: string;
  /** Plural — labels a collection (sidebar entry, utilization tab, page title). */
  displayNamePlural: string;
  description?: string;
  icon?: string;
  hasGeometry?: boolean;
  hasDirectoryProfile?: boolean;
  singleGroupMembership?: boolean;
}

export interface UpdateResourceTypeRequest {
  displayName?: string;
  displayNamePlural?: string;
  description?: string;
  icon?: string;
  /** Omit to leave as-is. Rejected for system types, whose behaviour the built-in pages need. */
  hasGeometry?: boolean;
  hasDirectoryProfile?: boolean;
  singleGroupMembership?: boolean;
  isActive?: boolean;
}

/** Optional per-field constraints. Enforced server-side; the form uses them as input hints. */

/** Key and data type are immutable once values exist — deactivate and re-create instead. */

const resourceTypesApi = createCrudApi<
  ResourceTypeInfo,
  CreateResourceTypeRequest,
  UpdateResourceTypeRequest
>({
  collectionPath: API_PATHS.RESOURCE_TYPES,
  itemPath: API_PATHS.resourceType,
});

export function getResourceTypes(isActive?: boolean): Promise<ResourceTypeInfo[]> {
  return resourceTypesApi.list(isActive === undefined ? undefined : { isActive: String(isActive) });
}

export function getResourceType(id: string): Promise<ResourceTypeInfo> {
  return resourceTypesApi.get(id);
}

export function createResourceType(request: CreateResourceTypeRequest): Promise<ResourceTypeInfo> {
  return resourceTypesApi.create(request);
}

export function updateResourceType(
  id: string,
  request: UpdateResourceTypeRequest,
): Promise<ResourceTypeInfo> {
  return resourceTypesApi.update(id, request);
}

/** Deletes the type, or deactivates it when resources still reference it. */
export function deleteResourceType(id: string): Promise<void> {
  return resourceTypesApi.remove(id);
}
