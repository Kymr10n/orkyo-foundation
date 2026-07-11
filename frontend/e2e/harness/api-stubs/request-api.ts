/*
 * Harness-only override for @foundation/src/lib/api/request-api.
 * RequestFormDialog calls getRequestChildren() to gate the leaf/group type
 * switch, and createRequest/moveRequest for the Children tab's inline
 * add/remove — none of which the dialog visual review exercises, so the
 * mutations are no-ops and getRequestChildren reads the fixture tree.
 * Wired via a vite alias in vite.config.ts (see permissions-stub.ts for the pattern).
 */
import type { Request } from "@foundation/src/types/requests";
import { requestChildrenFixture } from "../requests-fixtures";

// Not exercised by the dialog visual review — re-exported from the real
// module (relative import bypasses the alias) so the aliased module still
// satisfies every named import the app makes of it elsewhere.
export {
  getRequests,
  getConflictedRequests,
  updateRequest,
  deleteRequest,
  deleteRequestSubtree,
} from "../../../src/lib/api/request-api";

export async function getRequestChildren(requestId: string): Promise<Request[]> {
  return requestChildrenFixture.get(requestId) ?? [];
}

export async function createRequest(): Promise<Request> {
  throw new Error("createRequest is not wired in the harness");
}

export async function moveRequest(): Promise<void> {
  // no-op — not exercised by the dialog visual review
}
