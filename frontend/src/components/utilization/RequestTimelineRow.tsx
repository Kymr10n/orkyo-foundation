import React from "react";
import type { Conflict, Request } from "@foundation/src/types/requests";
import { clampToViewPercent } from "@foundation/src/domain/scheduling/schedule-selectors";
import { formatStatusLabel } from "@foundation/src/lib/utils/utils";
import type { TimeColumn } from "./scheduler-types";
import { TimelineRow } from "./TimelineRow";
import {
  REQUEST_BAR_BASE_CLASS,
  RequestBarLabel,
  RequestBarLayers,
  requestBarToneClass,
} from "./RequestBarVisual";

/** Bar geometry inside the 52px row: the same inner height a stations bar has, centred. */
const BAR_TOP_PX = 4;
const BAR_HEIGHT_PX = 44;

const EMPTY_CONFLICTS: readonly Conflict[] = [];

/**
 * One request's row on the Requests canvas.
 *
 * Built on the shared `TimelineRow` (label cell + column gridlines/tints) like the two resource
 * grids, but the row IS the request, so it carries exactly one bar: the request's own span,
 * clipped to the visible window. The bar is read-only — it opens the request and nothing else.
 * Rescheduling stays on the Stations grid and the Calendar, whose rows are resources and so can
 * say WHERE a drop lands; a row that is the request itself cannot.
 *
 * The look and the accessible name follow `ScheduledRequestOverlay` (the stations bar) so the
 * same request reads the same on both surfaces.
 */
export const RequestTimelineRow = React.memo(function RequestTimelineRow({
  request,
  columns,
  columnMinWidthPx,
  conflicts = EMPTY_CONFLICTS,
  onRequestClick,
  onRequestDoubleClick,
}: {
  request: Request;
  columns: readonly TimeColumn[];
  /** Zoomed column width; must match the shell's, see TimelineGridShell. */
  columnMinWidthPx: number;
  /** This request's conflicts from the page registry — they colour the bar and name its state. */
  conflicts?: readonly Conflict[];
  /** Single click / tap: the page opens the conflict detail on a red bar, the editor otherwise. */
  onRequestClick?: (requestId: string, position?: { x: number; y: number }) => void;
  /** Double-click, Enter or Space: always the editor. */
  onRequestDoubleClick?: (requestId: string) => void;
}) {
  const viewStartMs = columns[0].start.getTime();
  const viewEndMs = columns[columns.length - 1].end.getTime();

  const label = (
    <div className="min-w-0 flex-1">
      <div className="font-medium text-sm truncate" title={request.name}>
        {request.name}
      </div>
      <div className="text-xs text-muted-foreground truncate">{formatStatusLabel(request.status)}</div>
    </div>
  );

  return (
    <TimelineRow
      rowId={request.id}
      columns={columns}
      columnMinWidthPx={columnMinWidthPx}
      label={label}
      testId={`request-row-${request.id}`}
    >
      {request.startTs && request.endTs && (
        <RequestCanvasBar
          request={request}
          startMs={new Date(request.startTs).getTime()}
          endMs={new Date(request.endTs).getTime()}
          viewStartMs={viewStartMs}
          viewEndMs={viewEndMs}
          conflicts={conflicts}
          onRequestClick={onRequestClick}
          onRequestDoubleClick={onRequestDoubleClick}
        />
      )}
    </TimelineRow>
  );
});

function RequestCanvasBar({
  request,
  startMs,
  endMs,
  viewStartMs,
  viewEndMs,
  conflicts,
  onRequestClick,
  onRequestDoubleClick,
}: {
  request: Request;
  startMs: number;
  endMs: number;
  viewStartMs: number;
  viewEndMs: number;
  conflicts: readonly Conflict[];
  onRequestClick?: (requestId: string, position?: { x: number; y: number }) => void;
  onRequestDoubleClick?: (requestId: string) => void;
}) {
  const { leftPercent, widthPercent } = clampToViewPercent(startMs, endMs, viewStartMs, viewEndMs);
  const hasConflict = conflicts.length > 0;
  // The stations grid's palette: a scheduled request is an occupied block, a conflicted one is
  // overbooked. The legend over the canvas names exactly these two.
  const status = hasConflict ? "overbooked" : "assigned";
  const conflictCount = `${conflicts.length} conflict${conflicts.length === 1 ? "" : "s"}`;

  // The status word the colour conveys, so the cue is not colour-only (WCAG 1.4.1).
  const ariaLabel = hasConflict
    ? `${request.name}, Overbooked, ${conflictCount}. Open request.`
    : `${request.name}, Assigned. Open request.`;
  const title = hasConflict ? `${request.name} (${conflictCount})` : request.name;

  return (
    <div
      role="button"
      tabIndex={0}
      data-testid="request-canvas-bar"
      style={{
        left: `${leftPercent}%`,
        width: `${widthPercent}%`,
        top: `${BAR_TOP_PX}px`,
        height: `${BAR_HEIGHT_PX}px`,
      }}
      className={`absolute ${REQUEST_BAR_BASE_CLASS} ${requestBarToneClass(status)} cursor-pointer transition motion-reduce:transition-none hover:brightness-95 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring`}
      title={title}
      aria-label={ariaLabel}
      // Click and double-click are distinct actions; firing both from onClick made the
      // double-click handler the only one that ever ran (see ScheduledRequestOverlay). The click
      // point travels along so the caller can anchor the conflict popover to the bar that was hit.
      onClick={(e) => onRequestClick?.(request.id, { x: e.clientX, y: e.clientY })}
      onDoubleClick={() => onRequestDoubleClick?.(request.id)}
      onKeyDown={(e) => {
        if (e.target !== e.currentTarget) return;
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          // The editor, not the click action: a conflict popover anchors to a pointer position
          // a keypress does not have, and the editor's banner carries the same detail.
          onRequestDoubleClick?.(request.id);
        }
      }}
    >
      <RequestBarLayers status={status} />
      <RequestBarLabel request={request} hasConflict={hasConflict} />
    </div>
  );
}
