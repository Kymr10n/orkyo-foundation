/*
 * Harness-only override for @foundation/src/lib/api/resources-api.
 *
 * Two consumers, one filter: the request dialog's Resources tab resolves a station
 * assignment to "Bay 3", and RequestPeopleSection resolves person assignments to names.
 * Both call getResources with a resourceTypeKey, so the stub honours it rather than
 * returning one fixture set regardless — returning the wrong set is how an assignment
 * falls back to showing its raw resourceId.
 *
 * Wired via a vite alias in vite.config.ts (see permissions-stub.ts for the pattern).
 */
import type { ResourceInfo, ResourcesResponse } from "../../../src/lib/api/resources-api";
import { peopleFixture, spacesFixture } from "../requests-fixtures";

// Not exercised by the dialog reviews — re-exported from the real module (relative
// import bypasses the alias) so the aliased module still satisfies every named import
// the app makes of it elsewhere.
export {
  getResource,
  createResource,
  updateResource,
  deleteResource,
} from "../../../src/lib/api/resources-api";

const BY_TYPE: Record<string, ResourceInfo[]> = {
  space: spacesFixture,
  person: peopleFixture,
};

export async function getResources(
  params?: { resourceTypeKey?: string },
): Promise<ResourcesResponse> {
  // No key asked for: hand back everything, which is what the unfiltered callers want.
  const data = params?.resourceTypeKey
    ? (BY_TYPE[params.resourceTypeKey] ?? [])
    : [...spacesFixture, ...peopleFixture];
  return { data, total: data.length, page: 1, pageSize: data.length };
}
