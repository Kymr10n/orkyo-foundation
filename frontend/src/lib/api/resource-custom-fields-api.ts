/**
 * API client for the custom fields a tenant defines on a resource type.
 *
 * Custom fields are descriptive properties — a serial number, an asset tag, a link to the
 * datasheet. They are deliberately not criteria: nothing here is matchable, no request can
 * require one, and the scheduler never reads them. Values live on each resource in its
 * `customFields` document, keyed by the field's `key`.
 *
 * Defining a field is tenant governance, so writes are Admin-only server-side; reads are open
 * to any member, because editors have to see the fields they are filling in.
 */

import { API_PATHS } from '../core/api-paths';
import { createCrudApi } from './create-crud-api';

/**
 * `list` is the sixth type and behaves unlike the other five: it holds no value in the resource's
 * `customFields` document at all. Its rows live in their own instance, addressed by (resource,
 * field), so a whole-document replace on the resource form can never clobber them.
 */
export type CustomFieldDataType = 'text' | 'number' | 'boolean' | 'date' | 'url' | 'list';

/**
 * Every data type, with the words the UI uses for it. One source, because the list dialog's
 * badge and the edit dialog's "type is fixed" sentence have to agree.
 */
export const CUSTOM_FIELD_DATA_TYPES: readonly {
  value: CustomFieldDataType;
  label: string;
  hint: string;
}[] = [
  { value: 'text', label: 'Text', hint: 'A short line of text — a serial number, an asset tag.' },
  { value: 'number', label: 'Number', hint: 'A numeric value. Not matchable; use a criterion for that.' },
  { value: 'boolean', label: 'Yes / no', hint: 'A checkbox on the resource form.' },
  { value: 'date', label: 'Date', hint: 'A calendar date — purchased on, warranty until.' },
  { value: 'url', label: 'Link', hint: 'An http(s) address — a datasheet, a manual.' },
  {
    value: 'list',
    label: 'List',
    hint: 'Rows of their own, shaped by a list definition — a maintenance log, a set of parts.',
  },
];

export function customFieldDataTypeLabel(dataType: CustomFieldDataType): string {
  return CUSTOM_FIELD_DATA_TYPES.find((t) => t.value === dataType)?.label ?? dataType;
}

/** A value a resource holds for one custom field. `null` means the field is unfilled. */
export type CustomFieldValue = string | number | boolean | null;

export interface ResourceCustomField {
  id: string;
  resourceTypeId: string;
  /** Immutable after creation — it keys every value already stored for this field. */
  key: string;
  label: string;
  description?: string | null;
  /** Immutable after creation — changing it would reinterpret stored values. */
  dataType: CustomFieldDataType;
  /** For `list`: the definition its rows take their shape from. Fixed at creation. */
  listDefinitionId?: string | null;
  isRequired: boolean;
  sortOrder: number;
  /** Inactive fields leave the form but keep the values already captured. */
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateResourceCustomFieldRequest {
  key: string;
  label: string;
  description?: string;
  dataType: CustomFieldDataType;
  /** Required for `list`, rejected for every other type — the binding is part of the shape. */
  listDefinitionId?: string;
  isRequired?: boolean;
  sortOrder?: number;
}

/** Key and data type are absent on purpose: both are immutable once values can exist. */
export interface UpdateResourceCustomFieldRequest {
  label?: string;
  description?: string;
  isRequired?: boolean;
  sortOrder?: number;
  isActive?: boolean;
}

function apiFor(resourceTypeId: string) {
  return createCrudApi<
    ResourceCustomField,
    CreateResourceCustomFieldRequest,
    UpdateResourceCustomFieldRequest
  >({
    collectionPath: API_PATHS.resourceCustomFields(resourceTypeId),
    itemPath: (fieldId) => API_PATHS.resourceCustomField(resourceTypeId, fieldId),
  });
}

/** Every field defined on the type, already in form order. */
export function getResourceCustomFields(resourceTypeId: string): Promise<ResourceCustomField[]> {
  return apiFor(resourceTypeId).list();
}

export function createResourceCustomField(
  resourceTypeId: string,
  request: CreateResourceCustomFieldRequest,
): Promise<ResourceCustomField> {
  return apiFor(resourceTypeId).create(request);
}

export function updateResourceCustomField(
  resourceTypeId: string,
  fieldId: string,
  request: UpdateResourceCustomFieldRequest,
): Promise<ResourceCustomField> {
  return apiFor(resourceTypeId).update(fieldId, request);
}

/** Deletes the field and discards the values resources hold for it. */
export function deleteResourceCustomField(
  resourceTypeId: string,
  fieldId: string,
): Promise<void> {
  return apiFor(resourceTypeId).remove(fieldId);
}
