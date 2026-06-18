import React, { useMemo } from "react";
import type { ResourceInfo } from "@foundation/src/lib/api/resources-api";
import type { ResourceAssignmentInfo } from "@foundation/src/lib/api/resource-assignments-api";
import type { PersonUtilizationSegment } from "@foundation/src/domain/scheduling/utilization-segments";
import type { TimeColumn } from "./scheduler-types";
import { TimelineRow } from "./TimelineRow";
import { PersonSegmentBar } from "./PersonSegmentBar";

/** Per-segment overlap count + conflict flag, indexed parallel to `segments`. */
interface SegmentStat {
  count: number;
  hasConflict: boolean;
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
  // Parse assignment bounds once per assignment-set change, then compute each
  // segment's overlap count + conflict flag in a single pass. Previously both
  // were re-derived inside the segment map on every render, re-parsing every
  // assignment's start/end Date for each segment (O(segments × assignments)).
  const assignmentBounds = useMemo(
    () =>
      assignments.map((a) => ({
        id: a.id,
        startMs: new Date(a.startUtc).getTime(),
        endMs: new Date(a.endUtc).getTime(),
      })),
    [assignments],
  );

  const segmentStats = useMemo<SegmentStat[]>(
    () =>
      segments.map((segment) => {
        const segStart = new Date(segment.start).getTime();
        const segEnd = new Date(segment.end).getTime();
        let count = 0;
        let hasConflict = false;
        for (const a of assignmentBounds) {
          if (a.startMs < segEnd && a.endMs > segStart) {
            count++;
            if (!hasConflict && conflictedAssignmentIds.has(a.id)) hasConflict = true;
          }
        }
        return { count, hasConflict };
      }),
    [segments, assignmentBounds, conflictedAssignmentIds],
  );

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
        segments.map((segment, i) => (
          <PersonSegmentBar
            key={`${segment.start}-${segment.status}`}
            segment={segment}
            personName={person.name}
            viewStartMs={viewStartMs}
            viewEndMs={viewEndMs}
            assignmentCount={segmentStats[i].count}
            hasConflict={segmentStats[i].hasConflict}
            onClick={(s) => onSegmentClick(person, s)}
          />
        ))
      )}
    </TimelineRow>
  );
});
