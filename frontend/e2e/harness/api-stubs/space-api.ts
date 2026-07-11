/*
 * Harness-only override for @foundation/src/lib/api/space-api.
 * Backs useSpaces() with a single fixture space ("Bay 3") so the leaf-view
 * request's Resources tab resolves its assigned space to a name instead of
 * showing the raw resourceId.
 * Wired via a vite alias in vite.config.ts (see permissions-stub.ts for the pattern).
 */
import type { Space } from "@foundation/src/types/space";
import { spacesFixture } from "../requests-fixtures";

// Not exercised by the dialog visual review — re-exported from the real
// module (relative import bypasses the alias) so the aliased module still
// satisfies every named import the app makes of it elsewhere.
export { createSpace, updateSpace, deleteSpace } from "../../../src/lib/api/space-api";

export async function getSpaces(): Promise<Space[]> {
  return spacesFixture;
}
