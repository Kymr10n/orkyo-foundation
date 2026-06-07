import { AlertTriangle, XCircle } from "lucide-react";
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
  if (issues.length === 0) return null;
  const Icon = variant === "blocker" ? XCircle : AlertTriangle;
  const className = variant === "blocker" ? "text-destructive" : "text-amber-600";
  return (
    <ul className="space-y-0.5 mt-1">
      {issues.map((issue) => (
        // Composite key from the issue's identifying fields. A single validation
        // result never contains duplicates of (code + linked-entity ids), so this
        // is stable across re-renders even when issues are added/removed mid-typing.
        <li
          key={`${issue.code}:${issue.criterionId ?? ""}:${issue.conflictingAssignmentId ?? ""}:${issue.conflictingOffTimeId ?? ""}`}
          className={`flex items-start gap-1 text-xs ${className}`}
        >
          <Icon className="h-3.5 w-3.5 mt-0.5 shrink-0" />
          <span>
            {REASON_LABELS[issue.code] ?? issue.code}: {issue.message}
          </span>
        </li>
      ))}
    </ul>
  );
}
