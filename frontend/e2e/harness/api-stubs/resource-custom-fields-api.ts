/*
 * Harness-only override for @foundation/src/lib/api/resource-custom-fields-api.
 * Backs the ResourceEditDialog section's custom-field query with the Person
 * fixture set, so the form renders every field layout with no backend.
 * Wired via a vite alias in vite.config.ts (see permissions-stub.ts for the pattern).
 */
import type { ResourceCustomField } from "../../../src/lib/api/resource-custom-fields-api";
import { personCustomFieldsFixture } from "../resource-fixtures";

// Not exercised by the dialog review — re-exported from the real module (relative
// import bypasses the alias) so the aliased module still satisfies every named
// import the app makes of it elsewhere.
export {
  CUSTOM_FIELD_DATA_TYPES,
  customFieldDataTypeLabel,
  createResourceCustomField,
  updateResourceCustomField,
  deleteResourceCustomField,
} from "../../../src/lib/api/resource-custom-fields-api";

export async function getResourceCustomFields(): Promise<ResourceCustomField[]> {
  return personCustomFieldsFixture;
}
