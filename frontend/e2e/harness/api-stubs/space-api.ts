/*
 * Harness-only override for @foundation/src/lib/api/resources-api.
 * Backs usePlaceableResources() with a single fixture station ("Bay 3") so the leaf-view
 * request's Resources tab resolves its assigned resource to a name instead of showing the
 * raw resourceId.
 * Wired via a vite alias in vite.config.ts (see permissions-stub.ts for the pattern).
 */
import type { ResourceInfo, ResourcesResponse } from "@foundation/src/lib/api/resources-api";
import { spacesFixture } from "../requests-fixtures";

// Not exercised by the dialog visual review — re-exported from the real module (relative
// import bypasses the alias) so the aliased module still satisfies every named import the app
// makes of it elsewhere.
export {
  createResource,
  updateResource,
  deleteResource,
  getResource,
} from "../../../src/lib/api/resources-api";

export async function getResources(): Promise<ResourcesResponse> {
  const data: ResourceInfo[] = spacesFixture;
  return { data, total: data.length, page: 1, pageSize: data.length };
}
