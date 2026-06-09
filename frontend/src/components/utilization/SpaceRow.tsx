import React, { useMemo } from "react";
import { selectSpaceOverlapCount } from "@foundation/src/domain/scheduling/schedule-selectors";
import type { ValidationResult } from "@foundation/src/domain/scheduling/schedule-model";
import type { ScheduleIndex } from "@foundation/src/domain/scheduling/schedule-index";
import type { Request } from "@foundation/src/types/requests";
import type { Space } from "@foundation/src/types/space";
import { TimeCell } from "./TimeCell";
import { ScheduledRequestOverlay } from "./ScheduledRequestOverlay";
import { TimelineRow } from "./TimelineRow";
import type { TimeColumn } from "./scheduler-types";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";
import { overlapsOffTimeRange } from "./time-grid-utils";

// Constants for row height calculation
const BASE_ROW_HEIGHT = 52; // Base height for a single request
const REQUEST_HEIGHT = 44; // Height of each request block

export const SpaceRow = React.memo(function SpaceRow({
  space,
  columns,
  spaceRequests,
  scheduleIndex,
  validation,
  timeCursorTs,
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
  timeCursorTs: Date;
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

  // Preview entries for this space (sorted by startMs via the index)
  const spacePreviewEntries = scheduleIndex.bySpace.get(space.id) ?? [];

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
      renderColumnCell={(col) => (
        <TimeCell
          column={col}
          space={space}
          timeCursorTs={timeCursorTs}
          requests={spaceRequests}
          onRequestClick={onRequestClick}
          isOffTime={overlapsOffTimeRange(
            space.id,
            col.start.getTime(),
            col.end.getTime(),
            offTimeRanges,
          )}
        />
      )}
    >
      {/* Scheduled requests from preview (reflects draft bounds) */}
      {spacePreviewEntries.map((entry) => {
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
