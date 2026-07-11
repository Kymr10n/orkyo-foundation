/*
 * Harness-only override for @foundation/src/lib/api/site-api.
 * Backs useSites()/useIsMultiSite() with a single fixture site so the
 * RequestFormDialog's spaces query (scoped to the selected site) resolves.
 * Wired via a vite alias in vite.config.ts (see permissions-stub.ts for the pattern).
 */
import type { Site } from "@foundation/src/types/site";
import { sitesFixture } from "../requests-fixtures";

// Not exercised by the dialog visual review — re-exported from the real
// module (relative import bypasses the alias) so the aliased module still
// satisfies every named import the app makes of it elsewhere.
export { createSite, updateSite, deleteSite } from "../../../src/lib/api/site-api";

export async function getSites(): Promise<Site[]> {
  return sitesFixture;
}
