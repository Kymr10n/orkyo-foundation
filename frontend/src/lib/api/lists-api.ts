/**
 * API client for lists: tenant-defined tables that hang off a resource.
 *
 * Two halves, split by who may change what. A LIST DEFINITION is the reusable shape — the
 * columns and their types — and reshaping one changes what every editor is asked to fill in,
 * so those writes are Admin-only server-side. A LIST INSTANCE holds the rows, and filling one
 * in is ordinary editing, so row writes are open to editors. Reads are open to any member on
 * both, because the row form has to render the columns for the people entering data.
 *
 * Instances come in two kinds. A shared one is named, created by an admin, and referenced by
 * many resources through a lookup field, so editing a row propagates. A per-resource one
 * belongs to a single resource's list field and is created lazily by the resolver — a read
 * never brings one into existence, which is why `getResourceListInstance` can answer null.
 */

import { API_PATHS } from '../core/api-paths';
import { apiGet, apiPost } from '../core/api-client';
import { createCrudApi } from './create-crud-api';

/** The five scalar types a custom field has, plus `select` — which lists have and fields do not. */
export type ListColumnDataType = 'text' | 'number' | 'boolean' | 'date' | 'url' | 'select';

/**
 * Every column type, with the words the UI uses for it. One source, so the column dialog's
 * picker and the "type is fixed" sentence cannot drift apart.
 */
export const LIST_COLUMN_DATA_TYPES: readonly {
  value: ListColumnDataType;
  label: string;
  hint: string;
}[] = [
  { value: 'text', label: 'Text', hint: 'A short line of text — a note, a part name.' },
  { value: 'number', label: 'Number', hint: 'A numeric value — mileage, a price, a count.' },
  { value: 'boolean', label: 'Yes / no', hint: 'A checkbox in the row form.' },
  { value: 'date', label: 'Date', hint: 'A calendar date — serviced on, expires.' },
  { value: 'url', label: 'Link', hint: 'An http(s) address — a manual, an invoice.' },
  { value: 'select', label: 'Choice', hint: 'One of a set of options you define.' },
];

export function listColumnDataTypeLabel(dataType: ListColumnDataType): string {
  return LIST_COLUMN_DATA_TYPES.find((t) => t.value === dataType)?.label ?? dataType;
}

/** A single cell. `null` means unfilled — the same convention custom field values use. */
export type ListCellValue = string | number | boolean | null;

export interface ListColumn {
  id: string;
  listDefinitionId: string;
  /** Immutable after creation — it keys this column's value in every stored row. */
  key: string;
  label: string;
  description?: string | null;
  /** Immutable after creation — changing it would reinterpret stored cells. */
  dataType: ListColumnDataType;
  /** Declared options for `select`, null otherwise. Editable; stored rows are never rewritten. */
  options?: string[] | null;
  isRequired: boolean;
  sortOrder: number;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface ListDefinition {
  id: string;
  name: string;
  description?: string | null;
  isActive: boolean;
  /**
   * The column that identifies a row wherever one is shown as a single value. Null falls back to
   * the first active column, which is what every surface did before this existed.
   */
  displayColumnId?: string | null;
  createdAt: string;
  updatedAt: string;
  /** Populated when one definition is fetched; empty in the collection response. */
  columns: ListColumn[];
}

export type ListInstanceKind = 'shared' | 'resource';

export interface ListInstance {
  id: string;
  listDefinitionId: string;
  kind: ListInstanceKind;
  /** Set for shared instances; per-resource ones are anonymous and named by the field's label. */
  name?: string | null;
  resourceId?: string | null;
  fieldId?: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ListRow {
  id: string;
  listInstanceId: string;
  /** Cells keyed by column key. A column with no entry here is unfilled. */
  values: Record<string, ListCellValue>;
  createdAt: string;
  updatedAt: string;
}

export interface CreateListDefinitionRequest {
  name: string;
  description?: string;
}

export interface UpdateListDefinitionRequest {
  name?: string;
  description?: string;
  isActive?: boolean;
  /** Must be a column of this definition. */
  displayColumnId?: string;
  /** Removes the designation — null on displayColumnId cannot mean both "unchanged" and "cleared". */
  clearDisplayColumn?: boolean;
}

export interface CreateListColumnRequest {
  key: string;
  label: string;
  description?: string;
  dataType: ListColumnDataType;
  options?: string[];
  isRequired?: boolean;
  sortOrder?: number;
}

/** Key and data type are absent on purpose: both are immutable once cells can exist. */
export interface UpdateListColumnRequest {
  label?: string;
  description?: string;
  options?: string[];
  isRequired?: boolean;
  sortOrder?: number;
  isActive?: boolean;
}

export interface ListInstanceRequest {
  name: string;
}

export interface ListRowRequest {
  values: Record<string, ListCellValue>;
}

// ── definitions ─────────────────────────────────────────────────────────────

const definitionsApi = createCrudApi<
  ListDefinition,
  CreateListDefinitionRequest,
  UpdateListDefinitionRequest
>({
  collectionPath: API_PATHS.LIST_DEFINITIONS,
  itemPath: (definitionId) => API_PATHS.listDefinition(definitionId),
});

/** Active definitions by name; pass true to include retired ones. */
export function getListDefinitions(includeInactive = false): Promise<ListDefinition[]> {
  return apiGet<ListDefinition[]>(
    includeInactive
      ? `${API_PATHS.LIST_DEFINITIONS}?includeInactive=true`
      : API_PATHS.LIST_DEFINITIONS,
  );
}

/** One definition, with its columns in form order. */
export function getListDefinition(definitionId: string): Promise<ListDefinition> {
  return definitionsApi.get(definitionId);
}

export function createListDefinition(
  request: CreateListDefinitionRequest,
): Promise<ListDefinition> {
  return definitionsApi.create(request);
}

export function updateListDefinition(
  definitionId: string,
  request: UpdateListDefinitionRequest,
): Promise<ListDefinition> {
  return definitionsApi.update(definitionId, request);
}

/** Rejected with a 409 while any field or shared instance still references the definition. */
export function deleteListDefinition(definitionId: string): Promise<void> {
  return definitionsApi.remove(definitionId);
}

// ── columns ─────────────────────────────────────────────────────────────────

function columnsApi(definitionId: string) {
  return createCrudApi<ListColumn, CreateListColumnRequest, UpdateListColumnRequest>({
    collectionPath: API_PATHS.listColumns(definitionId),
    itemPath: (columnId) => API_PATHS.listColumn(definitionId, columnId),
  });
}

export function createListColumn(
  definitionId: string,
  request: CreateListColumnRequest,
): Promise<ListColumn> {
  return columnsApi(definitionId).create(request);
}

export function updateListColumn(
  definitionId: string,
  columnId: string,
  request: UpdateListColumnRequest,
): Promise<ListColumn> {
  return columnsApi(definitionId).update(columnId, request);
}

/** Deletes the column and discards the cells rows hold for it. */
export function deleteListColumn(definitionId: string, columnId: string): Promise<void> {
  return columnsApi(definitionId).remove(columnId);
}

// ── shared instances ────────────────────────────────────────────────────────

function sharedInstancesApi(definitionId: string) {
  return createCrudApi<ListInstance, ListInstanceRequest, ListInstanceRequest>({
    collectionPath: API_PATHS.sharedListInstances(definitionId),
    itemPath: (instanceId) => API_PATHS.sharedListInstance(definitionId, instanceId),
  });
}

export function getSharedListInstances(definitionId: string): Promise<ListInstance[]> {
  return sharedInstancesApi(definitionId).list();
}

export function createSharedListInstance(
  definitionId: string,
  request: ListInstanceRequest,
): Promise<ListInstance> {
  return sharedInstancesApi(definitionId).create(request);
}

export function updateSharedListInstance(
  definitionId: string,
  instanceId: string,
  request: ListInstanceRequest,
): Promise<ListInstance> {
  return sharedInstancesApi(definitionId).update(instanceId, request);
}

/** Rejected with a 409 while a lookup field still points at the instance. */
export function deleteSharedListInstance(definitionId: string, instanceId: string): Promise<void> {
  return sharedInstancesApi(definitionId).remove(instanceId);
}

// ── rows ────────────────────────────────────────────────────────────────────

/**
 * Rows are addressed by instance id for both kinds — the rows of a shared instance and of a
 * per-resource one are the same rows, so one path serves both.
 */
function rowsApi(instanceId: string) {
  return createCrudApi<ListRow, ListRowRequest, ListRowRequest>({
    collectionPath: API_PATHS.listRows(instanceId),
    itemPath: (rowId) => API_PATHS.listRow(instanceId, rowId),
  });
}

/** The instance itself — its kind and the definition it was built from. */
export function getListInstance(instanceId: string): Promise<ListInstance> {
  return apiGet<ListInstance>(API_PATHS.listInstance(instanceId));
}

export function getListRows(instanceId: string): Promise<ListRow[]> {
  return rowsApi(instanceId).list();
}

export function createListRow(instanceId: string, request: ListRowRequest): Promise<ListRow> {
  return rowsApi(instanceId).create(request);
}

export function updateListRow(
  instanceId: string,
  rowId: string,
  request: ListRowRequest,
): Promise<ListRow> {
  return rowsApi(instanceId).update(rowId, request);
}

/** Also drops the row from any resource that had selected it through a lookup field. */
export function deleteListRow(instanceId: string, rowId: string): Promise<void> {
  return rowsApi(instanceId).remove(rowId);
}

// ── the per-resource resolver ───────────────────────────────────────────────

/**
 * The instance behind one resource's list field, or null when it does not exist yet.
 *
 * Null is the ordinary state of a list nobody has written to: a read never creates a holder,
 * so an untouched list simply has none. Callers render an empty list and call
 * {@link ensureResourceListInstance} before the first write.
 */
export function getResourceListInstance(
  resourceId: string,
  fieldId: string,
): Promise<ListInstance | null> {
  // The endpoint answers 200 with a null body when the list is untouched, so an ordinary empty
  // case never arrives as an error to be told apart from a real one.
  return apiGet<ListInstance | null>(API_PATHS.resourceListInstance(resourceId, fieldId));
}

/** Get-or-create. Idempotent, so it is safe to call before every first write. */
export function ensureResourceListInstance(
  resourceId: string,
  fieldId: string,
): Promise<ListInstance> {
  return apiPost<ListInstance>(API_PATHS.resourceListInstance(resourceId, fieldId), undefined);
}
