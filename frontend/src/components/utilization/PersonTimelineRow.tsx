import React from "react";
import type { ResourceInfo } from "@foundation/src/lib/api/resources-api";
import type { ResourceAssignmentInfo } from "@foundation/src/lib/api/resource-assignments-api";
import type { PersonUtilizationSegment } from "@foundation/src/domain/scheduling/utilization-segments";
import type { TimeColumn } from "./scheduler-types";
import { TimelineRow } from "./TimelineRow";
import { PersonSegmentBar } from "./PersonSegmentBar";

function countOverlapping(
  assignments: ResourceAssignmentInfo[],
  segment: PersonUtilizationSegment,
): number {
  const segStart = new Date(segment.start).getTime();
  const segEnd = new Date(segment.end).getTime();
  return assignments.filter(
    (a) =>
      new Date(a.startUtc).getTime() < segEnd &&
      new Date(a.endUtc).getTime() > segStart,
  ).length;
}

function hasConflictInSegment(
  assignments: ResourceAssignmentInfo[],
  segment: PersonUtilizationSegment,
  conflictedIds: ReadonlySet<string>,
): boolean {
  if (conflictedIds.size === 0) return false;
  const segStart = new Date(segment.start).getTime();
  const segEnd = new Date(segment.end).getTime();
  return assignments.some(
    (a) =>
      conflictedIds.has(a.id) &&
      new Date(a.startUtc).getTime() < segEnd &&
      new Date(a.endUtc).getTime() > segStart,
  );
}

const EMPTY_SET: ReadonlySet<string> = new Set();

/**
 * One person's row in the People utilization timeline.
 *
 * Built on the shared `TimelineRow` (label cell + column gridlines/tints), with
 * read-only status segments (`PersonSegmentBar`) absolutely positioned against
 * the visible window. Segments aggregate booking status and are not editable.
 */
export const PersonTimelineRow = React.memo(function PersonTimelineRow({
  person,
  jobTitle,
  segments,
  isLoadingRow,
  overallPct,
  viewStartMs,
  viewEndMs,
  columns,
  assignments = [],
  conflictedAssignmentIds = EMPTY_SET,
  onSegmentClick,
}: {
  person: ResourceInfo;
  jobTitle?: string | null;
  segments: PersonUtilizationSegment[];
  isLoadingRow: boolean;
  overallPct: number;
  viewStartMs: number;
  viewEndMs: number;
  columns: readonly TimeColumn[];
  /** Non-cancelled assignments for this person in the view period, used for per-segment count badges. */
  assignments?: ResourceAssignmentInfo[];
  /** Assignment IDs that have a capability mismatch — drives the warning badge. */
  conflictedAssignmentIds?: ReadonlySet<string>;
  onSegmentClick: (person: ResourceInfo, segment: PersonUtilizationSegment) => void;
}) {
  const overallClass =
    overallPct > 100
      ? "text-red-500"
      : overallPct > 0
      ? "text-foreground"
      : "text-muted-foreground";

  const label = (
    <>
      <div className="min-w-0 flex-1">
        <div className="font-medium text-sm truncate" title={person.name}>
          {person.name}
        </div>
        <div className="text-xs text-muted-foreground truncate" title={jobTitle ?? ""}>
          {jobTitle ?? " "}
        </div>
      </div>
      <span
        className={`text-xs font-semibold tabular-nums shrink-0 ${overallClass}`}
        title={`Overall utilization: ${overallPct}%`}
      >
        {overallPct}%
      </span>
    </>
  );

  return (
    <TimelineRow
      rowId={person.id}
      columns={columns}
      label={label}
      testId={`person-row-${person.id}`}
    >
      {isLoadingRow ? (
        <div className="absolute inset-0 flex items-center px-3 text-xs text-muted-foreground italic">
          Loading…
        </div>
      ) : segments.length === 0 ? (
        <div className="absolute inset-0 flex items-center px-3 text-xs text-muted-foreground">
          No data
        </div>
      ) : (
        segments.map((segment) => (
          <PersonSegmentBar
            key={`${segment.start}-${segment.status}`}
            segment={segment}
            personName={person.name}
            viewStartMs={viewStartMs}
            viewEndMs={viewEndMs}
            assignmentCount={countOverlapping(assignments, segment)}
            hasConflict={hasConflictInSegment(assignments, segment, conflictedAssignmentIds)}
            onClick={(s) => onSegmentClick(person, s)}
          />
        ))
      )}
    </TimelineRow>
  );
});
