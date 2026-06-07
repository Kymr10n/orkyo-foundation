/**
 * Maps the backend's authoritative assignment-validation result into the FE
 * `Conflict` shape — the single source of truth for *capability* conflicts
 * (does a space satisfy a request's requirements).
 *
 * Scheduling conflicts (overlap, capacity, min-duration) are intentionally NOT
 * derived here: those are computed client-side by `evaluateSchedule` so the grid
 * can show them live while dragging, before anything is committed. We therefore
 * ignore overbook / off-time / weekend codes to avoid double-counting.
 */

import type { Conflict } from "@foundation/src/types/requests";
import type {
  AssignmentValidationBatchItem,
  ValidationIssue,
} from "@foundation/src/lib/api/resource-assignments-api";

/** Backend reason codes that mean the space fails the request's capability requirements. */
const CAPABILITY_REASON_CODES: ReadonlySet<string> = new Set([
  "capability.missing",
  "resource.type-mismatch",
]);

/**
 * Extracts capability conflicts from one batch-validation entry. Returns `[]`
 * when the entry carries no request id or no capability blockers.
 */
export function capabilityConflictsFromValidation(
  item: AssignmentValidationBatchItem,
): Conflict[] {
  if (!item.requestId) return [];
  return item.result.blockers
    .filter((issue) => CAPABILITY_REASON_CODES.has(issue.code))
    .map((issue) => issueToConflict(item.requestId!, issue));
}

function issueToConflict(requestId: string, issue: ValidationIssue): Conflict {
  const detail = issue.details ? `: ${issue.details}` : "";
  return {
    id: `${requestId}-${issue.criterionId ?? issue.code}-capability`,
    kind: "connector_mismatch",
    severity: "error",
    message:
      issue.code === "resource.type-mismatch"
        ? `Space cannot satisfy this requirement${detail}`
        : `Space does not satisfy a required capability${detail}`,
  };
}
