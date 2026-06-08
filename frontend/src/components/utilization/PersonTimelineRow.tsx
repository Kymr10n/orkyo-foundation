import React from "react";
import type { ResourceInfo } from "@foundation/src/lib/api/resources-api";
import type { ResourceAssignmentInfo } from "@foundation/src/lib/api/resource-assignments-api";
import type { PersonUtilizationSegment } from "@foundation/src/domain/scheduling/utilization-segments";
import type { TimeColumn } from "./scheduler-types";
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

/**
 * One person's row in the People utilization timeline.
 *
 * The label cell (name / job title / overall %) is unchanged from the former
 * heatmap row; the timeline area now renders read-only status segments
 * (`PersonSegmentBar`) positioned against the visible window instead of a
 * fixed grid of colored cells.
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
  onSegmentClick,
}: {
  person: ResourceInfo;
  jobTitle?: string | null;
  segments: PersonUtilizationSegment[];
  isLoadingRow: boolean;
  overallPct: number;
  viewStartMs: number;
  viewEndMs: number;
  /** Time columns from PeopleUtilizationGrid — used for gridlines and off-day tints. */
  columns?: readonly TimeColumn[];
  /** Non-cancelled assignments for this person in the view period, used for per-segment count badges. */
  assignments?: ResourceAssignmentInfo[];
  onSegmentClick: (person: ResourceInfo, segment: PersonUtilizationSegment) => void;
}) {
  const overallClass =
    overallPct > 100
      ? "text-red-500"
      : overallPct > 0
      ? "text-foreground"
      : "text-muted-foreground";

  return (
    <div
      className="flex border-b hover:bg-accent/30 transition-colors"
      data-testid={`person-row-${person.id}`}
    >
      {/* Label cell — identical to the former heatmap row */}
      <div className="w-52 flex-shrink-0 px-3 py-2 border-r flex items-center gap-2">
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
      </div>

      {/* Timeline track — relative container for absolutely-positioned segments.
          Matches the Spaces row rhythm (52px row, 36px bars centred with 8px gaps). */}
      <div className="flex-1 relative" style={{ minHeight: "52px" }}>
        {/* Column gridlines + off-day tints — rendered behind segment bars */}
        {columns?.map((col, i) => {
          const colStart = col.start.getTime();
          const colEnd   = col.end.getTime();
          const span = viewEndMs - viewStartMs;
          const left  = Math.max(0, (colStart - viewStartMs) / span * 100);
          const right = Math.max(0, (viewEndMs - colEnd)   / span * 100);
          const bg = col.isWeekend || col.isGlobalOffTime ? 'bg-destructive/15' : '';
          return (
            <div
              key={i}
              className={`absolute inset-y-0 border-r pointer-events-none ${bg}`}
              style={{ left: `${left}%`, right: `${right}%` }}
            />
          );
        })}
        {isLoadingRow ? (
          <div className="px-3 py-2 text-xs text-muted-foreground italic">Loading…</div>
        ) : segments.length === 0 ? (
          <div className="px-3 py-2 text-xs text-muted-foreground">No data</div>
        ) : (
          segments.map((segment) => (
            <PersonSegmentBar
              key={`${segment.start}-${segment.status}`}
              segment={segment}
              personName={person.name}
              viewStartMs={viewStartMs}
              viewEndMs={viewEndMs}
              assignmentCount={countOverlapping(assignments, segment)}
              onClick={(s) => onSegmentClick(person, s)}
            />
          ))
        )}
      </div>
    </div>
  );
});
