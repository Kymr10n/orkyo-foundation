/**
 * API client for resource types and their custom field definitions.
 *
 * Resource types are the tenant's catalogue of manageable things. Three are seeded and
 * system-owned (space, person, tool); tenants may define their own (car, camera, …) and
 * give each a set of custom fields whose values live on each resource's `metadata` document.
 */

import { apiGet } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import { createCrudApi } from './create-crud-api';

/** Custom field data types. Mirrors `ResourceFieldDataTypes` on the backend. */
export const RESOURCE_FIELD_DATA_TYPES = ['text', 'number', 'boolean', 'date', 'select'] as const;
export type ResourceFieldDataType = (typeof RESOURCE_FIELD_DATA_TYPES)[number];

export interface ResourceTypeInfo {
  id: string;
  key: string;
  displayName: string;
  description?: string;
  /** lucide-react icon name; unknown or absent names fall back to a default. */
  icon?: string | null;
  /** Seeded types (space, person, tool) — identity and lifecycle are read-only. */
  isSystem: boolean;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateResourceTypeRequest {
  key: string;
  displayName: string;
  description?: string;
  icon?: string;
}

export interface UpdateResourceTypeRequest {
  displayName?: string;
  description?: string;
  icon?: string;
  isActive?: boolean;
}

/** Optional per-field constraints. Enforced server-side; the form uses them as input hints. */
export interface ResourceFieldValidation {
  min?: number;
  max?: number;
  regex?: string;
  maxLength?: number;
}

export interface ResourceFieldOptions {
  values: string[];
}

export interface ResourceTypeFieldInfo {
  id: string;
  resourceTypeId: string;
  key: string;
  label: string;
  description?: string;
  dataType: ResourceFieldDataType;
  options?: ResourceFieldOptions;
  validation?: ResourceFieldValidation;
  isRequired: boolean;
  sortOrder: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateResourceTypeFieldRequest {
  key: string;
  label: string;
  description?: string;
  dataType: ResourceFieldDataType;
  options?: ResourceFieldOptions;
  validation?: ResourceFieldValidation;
  isRequired?: boolean;
  sortOrder?: number;
}

/** Key and data type are immutable once values exist — deactivate and re-create instead. */
export interface UpdateResourceTypeFieldRequest {
  label?: string;
  description?: string;
  options?: ResourceFieldOptions;
  validation?: ResourceFieldValidation;
  isRequired?: boolean;
  sortOrder?: number;
  isActive?: boolean;
}

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

// ── field definitions (nested collection — not the flat CRUD shape) ──────────

export function getResourceTypeFields(
  resourceTypeId: string,
  includeInactive = false,
): Promise<ResourceTypeFieldInfo[]> {
  const path = API_PATHS.resourceTypeFields(resourceTypeId);
  return apiGet<ResourceTypeFieldInfo[]>(includeInactive ? `${path}?includeInactive=true` : path);
}

const fieldsApi = (resourceTypeId: string) =>
  createCrudApi<ResourceTypeFieldInfo, CreateResourceTypeFieldRequest, UpdateResourceTypeFieldRequest>({
    collectionPath: API_PATHS.resourceTypeFields(resourceTypeId),
    itemPath: (fieldId) => API_PATHS.resourceTypeField(resourceTypeId, fieldId),
  });

export function createResourceTypeField(
  resourceTypeId: string,
  request: CreateResourceTypeFieldRequest,
): Promise<ResourceTypeFieldInfo> {
  return fieldsApi(resourceTypeId).create(request);
}

export function updateResourceTypeField(
  resourceTypeId: string,
  fieldId: string,
  request: UpdateResourceTypeFieldRequest,
): Promise<ResourceTypeFieldInfo> {
  return fieldsApi(resourceTypeId).update(fieldId, request);
}

/** Deactivates the definition. Stored values are retained but no longer validated or shown. */
export function deactivateResourceTypeField(
  resourceTypeId: string,
  fieldId: string,
): Promise<void> {
  return fieldsApi(resourceTypeId).remove(fieldId);
}
