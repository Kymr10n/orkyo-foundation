import React, { useMemo } from "react";
import { GripVertical } from "lucide-react";
import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { selectSpaceOverlapCount } from "@foundation/src/domain/scheduling/schedule-selectors";
import type { PreviewSchedule, ValidationResult } from "@foundation/src/domain/scheduling/schedule-model";
import type { ScheduleIndex } from "@foundation/src/domain/scheduling/schedule-index";
import type { Request } from "@foundation/src/types/requests";
import type { Space } from "@foundation/src/types/space";
import { TimeCell } from "./TimeCell";
import { ScheduledRequestOverlay } from "./ScheduledRequestOverlay";
import { OffTimeOverlay } from "./OffTimeOverlay";
import type { TimeColumn } from "./scheduler-types";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";

// Constants for row height calculation
const BASE_ROW_HEIGHT = 52; // Base height for a single request
const REQUEST_HEIGHT = 44; // Height of each request block

export const SpaceRow = React.memo(function SpaceRow({
  space,
  columns,
  requests,
  previewSchedule: _previewSchedule,
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
  requests: Request[];
  previewSchedule: PreviewSchedule;
  scheduleIndex: ScheduleIndex;
  validation: ValidationResult;
  timeCursorTs: Date;
  onRequestClick: (requestId: string) => void;
  onRequestDoubleClick?: (requestId: string) => void;
  onRequestResize?: (requestId: string, startTs: string, endTs: string) => void;
  offTimeRanges?: readonly OffTimeRange[];
}) {
  // Pre-filter requests for this space (stable reference for TimeCell memoization)
  const spaceRequests = useMemo(
    () => requests.filter((r) => r.spaceId === space.id),
    [requests, space.id]
  );

  // Build a requestId→Request map for O(1) lookup from preview entries
  const requestsById = useMemo(
    () => new Map(requests.map((r) => [r.id, r])),
    [requests]
  );

  // Preview entries for this space (sorted by startMs via the index)
  const spacePreviewEntries = scheduleIndex.bySpace.get(space.id) ?? [];

  // Calculate row height from the preview index (reflects draft bounds)
  const maxOverlaps = selectSpaceOverlapCount(scheduleIndex, space.id);
  const rowHeight = BASE_ROW_HEIGHT + (maxOverlaps - 1) * REQUEST_HEIGHT;

  // Sortable setup
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({
    id: space.id,
    data: { type: 'space-row' }
  });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
  };

  return (
    <div ref={setNodeRef} style={style} className="flex border-b hover:bg-accent/30 transition-colors">
      {/* Space label with drag handle */}
      <div
        className="w-40 flex-shrink-0 p-2 border-r flex items-center gap-2"
      >
        <div
          {...attributes}
          {...listeners}
          className="cursor-grab active:cursor-grabbing text-muted-foreground hover:text-foreground"
        >
          <GripVertical className="h-4 w-4" />
        </div>
        <div className="min-w-0 flex-1">
          <div className="font-medium text-sm truncate">{space.code}</div>
          <div className="text-xs text-muted-foreground truncate">
            {space.name}
          </div>
        </div>
      </div>

      {/* Time cells */}
      <div className="flex-1 flex relative" style={{ minHeight: `${rowHeight}px` }}>
        {columns.map((col, idx) => (
          <TimeCell
            key={idx}
            column={col}
            space={space}
            timeCursorTs={timeCursorTs}
            requests={spaceRequests}
            onRequestClick={onRequestClick}
          />
        ))}
        {/* Render off-time overlays */}
        {offTimeRanges.map((ot) => (
          <OffTimeOverlay
            key={ot.id}
            offTime={ot}
            columns={columns}
            spaceId={space.id}
          />
        ))}
        {/* Render scheduled requests from preview (reflects draft bounds) */}
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
      </div>
    </div>
  );
});
