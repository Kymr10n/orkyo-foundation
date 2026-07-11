/*
 * Harness-only override for @foundation/src/lib/api/resource-assignments-api.
 * RequestPeopleSection loads a request's assignments via getAssignmentsByRequest
 * and mutates via createAssignment/cancelAssignment/validateAssignment — all
 * stubbed here against the leaf-view fixture. hardBlockers/softBlockers/
 * SOFT_BLOCKER_CODES are pure logic (no network), so they're re-exported from
 * the real module via a relative import, bypassing the alias — same pattern as
 * permissions-stub.ts.
 */
import type {
  ResourceAssignmentInfo,
  ValidationResult,
} from "../../../src/lib/api/resource-assignments-api";
import { leafViewAssignmentsFixture } from "../requests-fixtures";

export {
  hardBlockers,
  softBlockers,
  SOFT_BLOCKER_CODES,
  getAssignmentsByResource,
  getAssignmentsByResourceType,
  validateAssignmentsBatch,
} from "../../../src/lib/api/resource-assignments-api";

export async function getAssignmentsByRequest(requestId: string): Promise<ResourceAssignmentInfo[]> {
  return leafViewAssignmentsFixture.filter((a) => a.requestId === requestId);
}

export async function createAssignment(): Promise<ResourceAssignmentInfo> {
  throw new Error("createAssignment is not wired in the harness");
}

export async function cancelAssignment(): Promise<void> {
  // no-op — not exercised by the dialog visual review
}

export async function validateAssignment(): Promise<ValidationResult> {
  return { severity: "ok", blockers: [], warnings: [] };
}
