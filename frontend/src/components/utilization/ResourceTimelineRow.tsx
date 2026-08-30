import React, { useMemo } from "react";
import type { ResourceInfo } from "@foundation/src/lib/api/resources-api";
import type { ResourceAssignmentInfo } from "@foundation/src/lib/api/resource-assignments-api";
import type { ResourceUtilizationSegment } from "@foundation/src/domain/scheduling/utilization-segments";
import type { TimeColumn } from "./scheduler-types";
import { TimelineRow } from "./TimelineRow";
import { ResourceSegmentBar } from "./ResourceSegmentBar";

/** Per-segment overlap count + conflict flag, indexed parallel to `segments`. */
interface SegmentStat {
  count: number;
  hasConflict: boolean;
}

const EMPTY_SET: ReadonlySet<string> = new Set();

/**
 * One resource's row in a resource-type utilization timeline.
 *
 * Built on the shared `TimelineRow` (label cell + column gridlines/tints), with
 * read-only status segments (`ResourceSegmentBar`) absolutely positioned against
 * the visible window. Segments aggregate booking status and are not editable.
 */
export const ResourceTimelineRow = React.memo(function ResourceTimelineRow({
  resource,
  secondaryLabel,
  segments,
  isLoadingRow,
  overallPct,
  viewStartMs,
  viewEndMs,
  columns,
  assignments = [],
  conflictedAssignmentIds = EMPTY_SET,
  onSegmentClick,
  onOpenSchedule,
}: {
  resource: ResourceInfo;
  /** Line under the name: a job title for people, the description for other types. */
  secondaryLabel?: string | null;
  segments: ResourceUtilizationSegment[];
  isLoadingRow: boolean;
  overallPct: number;
  viewStartMs: number;
  viewEndMs: number;
  columns: readonly TimeColumn[];
  /** Non-cancelled assignments for this resource in the view period, used for per-segment count badges. */
  assignments?: ResourceAssignmentInfo[];
  /** Assignment IDs that have a capability mismatch — drives the warning badge. */
  conflictedAssignmentIds?: ReadonlySet<string>;
  onSegmentClick: (resource: ResourceInfo, segment: ResourceUtilizationSegment) => void;
  /** Opens this resource's own schedule calendar from the label cell. */
  onOpenSchedule?: (resource: ResourceInfo) => void;
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

  const nameBlock = (
    <>
      <div className="font-medium text-sm truncate" title={resource.name}>
        {resource.name}
      </div>
      <div className="text-xs text-muted-foreground truncate" title={secondaryLabel ?? ""}>
        {secondaryLabel ?? " "}
      </div>
    </>
  );

  const label = (
    <>
      {onOpenSchedule ? (
        <button
          type="button"
          // The segments are a read-only aggregate; the label is the resource itself, so it
          // opens that resource's own calendar.
          onClick={(e) => {
            e.stopPropagation();
            onOpenSchedule(resource);
          }}
          className="min-w-0 flex-1 text-left hover:underline focus-visible:underline focus-visible:outline-hidden"
          aria-label={`Open the schedule for ${resource.name}`}
        >
          {nameBlock}
        </button>
      ) : (
        <div className="min-w-0 flex-1">{nameBlock}</div>
      )}
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
      rowId={resource.id}
      columns={columns}
      label={label}
      testId={`resource-row-${resource.id}`}
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
          <ResourceSegmentBar
            key={`${segment.start}-${segment.status}`}
            segment={segment}
            resourceName={resource.name}
            viewStartMs={viewStartMs}
            viewEndMs={viewEndMs}
            assignmentCount={segmentStats[i].count}
            hasConflict={segmentStats[i].hasConflict}
            onClick={(s) => onSegmentClick(resource, s)}
          />
        ))
      )}
    </TimelineRow>
  );
});
