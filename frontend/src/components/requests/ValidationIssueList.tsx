import {
  StatusMessageList,
  type StatusItem,
} from "@foundation/src/components/ui/status-indicator";
import type {
  ValidationIssue,
  ValidationReasonCode,
} from "@foundation/src/lib/api/resource-assignments-api";

/** Human-readable labels for assignment validation reason codes. */
export const REASON_LABELS: Record<ValidationReasonCode, string> = {
  "resource.not-found": "Resource not found",
  "resource.inactive": "Resource is inactive",
  "resource.type-mismatch": "Resource type mismatch",
  "capability.missing": "Required capability missing",
  "offtime.overlap": "Overlaps with off-time",
  "assignment.overbooked": "Resource is overbooked",
  "nonworking.weekend": "Overlaps with a non-working weekend",
  "nonworking.holiday": "Overlaps with a holiday",
  "allocation-mode.invalid": "Invalid allocation mode",
  "allocation-percent.invalid": "Invalid allocation percent",
  "site.mismatch-space": "Space is at a different site",
  "site.mismatch-person": "Currently at a different site",
  "site.cross-not-allowed": "Not available for cross-site work",
};

/**
 * Renders a list of assignment validation issues (blockers or warnings).
 * Shared by the request editor's People section and the People-utilization
 * Person Assignment dialog so the labels/icons stay consistent.
 */
export function ValidationIssueList({
  issues,
  variant,
}: {
  issues: ValidationIssue[];
  variant: "blocker" | "warning";
}) {
  // Map onto the shared severity model so blockers/warnings render through the same
  // StatusMessageList as every other dialog indicator (one icon/colour system).
  const items: StatusItem[] = issues.map((issue) => ({
    // Composite key from the issue's identifying fields. A single validation result
    // never contains duplicates of (code + linked-entity ids), so this is stable
    // across re-renders even when issues are added/removed mid-typing.
    id: `${issue.code}:${issue.criterionId ?? ""}:${issue.conflictingAssignmentId ?? ""}:${issue.conflictingOffTimeId ?? ""}`,
    message: `${REASON_LABELS[issue.code] ?? issue.code}: ${issue.message}`,
    severity: variant === "blocker" ? "error" : "warning",
  }));
  return <StatusMessageList items={items} />;
}
