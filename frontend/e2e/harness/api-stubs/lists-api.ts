/*
 * Harness-only override for @foundation/src/lib/api/lists-api.
 * Backs the ResourceEditDialog section's Department lookup field: the picker
 * needs an instance, the definition behind it, and its rows.
 * Wired via a vite alias in vite.config.ts (see permissions-stub.ts for the pattern).
 */
import type { ListDefinition, ListInstance, ListRow } from "../../../src/lib/api/lists-api";
import {
  departmentDefinitionFixture,
  departmentInstanceFixture,
  departmentRowsFixture,
} from "../resource-fixtures";

// Not exercised by the dialog review — re-exported from the real module (relative
// import bypasses the alias) so the aliased module still satisfies every named
// import the app makes of it elsewhere.
export {
  LIST_COLUMN_DATA_TYPES,
  listColumnDataTypeLabel,
  getListDefinitions,
  createListDefinition,
  updateListDefinition,
  deleteListDefinition,
  createListColumn,
  updateListColumn,
  deleteListColumn,
  getSharedListInstances,
  createSharedListInstance,
  updateSharedListInstance,
  deleteSharedListInstance,
  createListRow,
  updateListRow,
  deleteListRow,
  getResourceListInstance,
  ensureResourceListInstance,
} from "../../../src/lib/api/lists-api";

export async function getListDefinition(): Promise<ListDefinition> {
  return departmentDefinitionFixture;
}

export async function getListInstance(): Promise<ListInstance> {
  return departmentInstanceFixture;
}

export async function getListRows(): Promise<ListRow[]> {
  return departmentRowsFixture;
}
