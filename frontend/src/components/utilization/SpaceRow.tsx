import React, { useCallback, useMemo } from "react";
import { useDroppable } from "@dnd-kit/core";
import { selectSpaceOverlapCount, isOutsideView } from "@foundation/src/domain/scheduling/schedule-selectors";
import type { PreviewEntry, ValidationResult } from "@foundation/src/domain/scheduling/schedule-model";
import type { ScheduleIndex } from "@foundation/src/domain/scheduling/schedule-index";
import type { Request } from "@foundation/src/types/requests";
import type { Space } from "@foundation/src/types/space";
import { ScheduledRequestOverlay } from "./ScheduledRequestOverlay";
import { TimelineRow } from "./TimelineRow";
import type { TimeColumn } from "./scheduler-types";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";
import { overlapsOffTimeRange } from "./time-grid-utils";

// Constants for row height calculation
const BASE_ROW_HEIGHT = 52; // Base height for a single request
const REQUEST_HEIGHT = 44; // Height of each request block

const EMPTY_ENTRIES: readonly PreviewEntry[] = [];

export const SpaceRow = React.memo(function SpaceRow({
  space,
  columns,
  spaceRequests,
  scheduleIndex,
  validation,
  onRequestClick,
  onRequestDoubleClick,
  onRequestResize,
  offTimeRanges = [],
}: {
  space: Space;
  columns: TimeColumn[];
  spaceRequests: Request[];
  scheduleIndex: ScheduleIndex;
  validation: ValidationResult;
  onRequestClick: (requestId: string) => void;
  onRequestDoubleClick?: (requestId: string) => void;
  onRequestResize?: (requestId: string, startTs: string, endTs: string) => void;
  offTimeRanges?: readonly OffTimeRange[];
}) {
  // Build a requestId→Request map for O(1) lookup from preview entries
  const requestsById = useMemo(
    () => new Map(spaceRequests.map((r) => [r.id, r])),
    [spaceRequests]
  );

  // A single droppable per row (not per cell). The exact column is resolved
  // from the pointer x-position at drop time in UtilizationPage.handleDragEnd,
  // so we carry the column start timestamps in the drop data. Collapsing
  // rows×columns droppables to one-per-row is what keeps the grid fast: dnd-kit
  // clones its container Map on every registration, so per-cell droppables made
  // mounting O(n²).
  const columnStartsMs = useMemo(
    () => columns.map((c) => c.start.getTime()),
    [columns]
  );
  const { setNodeRef: setTrackRef, isOver } = useDroppable({
    id: `track-${space.id}`,
    data: { type: "space-track", resourceId: space.id, columnStartsMs },
  });

  const isCellOffTime = useCallback(
    (col: TimeColumn) =>
      overlapsOffTimeRange(
        space.id,
        col.start.getTime(),
        col.end.getTime(),
        offTimeRanges,
      ),
    [space.id, offTimeRanges],
  );

  // Only the bars overlapping the visible time window are rendered. Off-view
  // entries used to mount a ScheduledRequestOverlay that registered a
  // useDraggable before returning null — so a visible row paid for the space's
  // entire scheduling history, not just what's on screen. Filtering here keeps
  // mounted overlays + dnd draggables proportional to the visible window.
  const viewStartMs = columns[0].start.getTime();
  const viewEndMs = columns[columns.length - 1].end.getTime();
  const visibleEntries = useMemo(() => {
    const entries = scheduleIndex.bySpace.get(space.id);
    if (!entries) return EMPTY_ENTRIES;
    return entries.filter((e) => !isOutsideView(e, viewStartMs, viewEndMs));
  }, [scheduleIndex, space.id, viewStartMs, viewEndMs]);

  // Calculate row height from the preview index (reflects draft bounds)
  const maxOverlaps = selectSpaceOverlapCount(scheduleIndex, space.id);
  const rowHeight = BASE_ROW_HEIGHT + (maxOverlaps - 1) * REQUEST_HEIGHT;

  return (
    <TimelineRow
      rowId={space.id}
      columns={columns}
      sortable
      sortableData={{ type: "space-row" }}
      minHeight={rowHeight}
      label={
        <div className="min-w-0 flex-1">
          <div className="font-medium text-sm truncate">{space.code}</div>
          <div className="text-xs text-muted-foreground truncate">{space.name}</div>
        </div>
      }
      isOffTime={isCellOffTime}
      trackRef={setTrackRef}
      trackClassName={isOver ? "bg-blue-100 dark:bg-blue-900/20" : ""}
    >
      {/* Scheduled requests from preview (reflects draft bounds) */}
      {visibleEntries.map((entry) => {
        const request = requestsById.get(entry.requestId);
        if (!request) return null;
        return (
          <ScheduledRequestOverlay
            key={entry.requestId}
            request={request}
            entry={entry}
            columns={columns}
            scheduleIndex={scheduleIndex}
            validation={validation}
            onRequestClick={onRequestClick}
            onRequestDoubleClick={onRequestDoubleClick}
            onRequestResize={onRequestResize}
          />
        );
      })}
    </TimelineRow>
  );
});
