/*
 * Harness-only override for @foundation/src/lib/api/resource-types-api.
 * The Resources tab (RequestTargetTypesField, the per-type pickers and
 * RequestPeopleSection) reads the tenant's resource types to decide what to offer
 * and which of them carry a directory profile. Backed here by the station and
 * person fixtures.
 * Wired via a vite alias in vite.config.ts (see permissions-stub.ts for the pattern).
 */
import type { ResourceTypeInfo } from "../../../src/lib/api/resource-types-api";
import { personTypeFixture, stationTypeFixture } from "../resource-fixtures";

// Not exercised by the dialog reviews — re-exported from the real module (relative
// import bypasses the alias) so the aliased module still satisfies every named
// import the app makes of it elsewhere.
export {
  createResourceType,
  updateResourceType,
  deleteResourceType,
} from "../../../src/lib/api/resource-types-api";

const TYPES: ResourceTypeInfo[] = [stationTypeFixture, personTypeFixture];

export async function getResourceTypes(): Promise<ResourceTypeInfo[]> {
  return TYPES;
}

export async function getResourceType(id: string): Promise<ResourceTypeInfo> {
  const type = TYPES.find((t) => t.id === id);
  if (!type) throw new Error(`No fixture resource type ${id}`);
  return type;
}
