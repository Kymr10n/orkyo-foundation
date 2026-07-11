/*
 * Harness-only override for @foundation/src/lib/api/resources-api.
 * RequestPeopleSection calls getResources({ resourceTypeKey: 'person' }) to
 * resolve assignment resourceIds to names — backed here by the 2-person
 * fixture regardless of filter, since the harness only ever wants people.
 * Wired via a vite alias in vite.config.ts (see permissions-stub.ts for the pattern).
 */
import type { ResourcesResponse } from "../../../src/lib/api/resources-api";
import { peopleFixture } from "../requests-fixtures";

// Not exercised by the dialog visual review — re-exported from the real
// module (relative import bypasses the alias) so the aliased module still
// satisfies every named import the app makes of it elsewhere.
export {
  getResource,
  createResource,
  updateResource,
  deleteResource,
} from "../../../src/lib/api/resources-api";

export async function getResources(): Promise<ResourcesResponse> {
  return { data: peopleFixture, total: peopleFixture.length, page: 1, pageSize: peopleFixture.length };
}
