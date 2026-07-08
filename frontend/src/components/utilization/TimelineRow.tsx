import React from "react";
import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import type { TimeColumn } from "./scheduler-types";
import { PROBLEM_HATCH_CLASS, OFFTIME_TINT_CLASS } from "./schedule-colors";

/**
 * Shared row chrome for both utilization grids (Spaces + People).
 *
 * Owns the `w-52` label cell, the `flex-1 flex relative` time track, and the
 * per-column gridline + off-day tint cells. The actual allocation bars
 * (`ScheduledRequestOverlay` for Spaces, `PersonSegmentBar` for People) are
 * passed as `children` and rendered absolutely on top of the column cells.
 *
 * This centralises the gridline/tint layer for both grids and guarantees the
 * body columns line up with the shared header. Cells are plain presentational
 * divs — the drop target is a single row-level droppable (Spaces attaches it
 * via `trackRef`), not one droppable per cell.
 */

/** Tailwind tint for a column cell — single source for both grids. */
export function columnTintClass(col: TimeColumn): string {
  if (col.isWeekend || col.isGlobalOffTime) return OFFTIME_TINT_CLASS;
  if (col.isOutsideWorkingHours) return "bg-muted/80";
  return "";
}

interface TimelineRowProps {
  /** Stable id; used for the sortable handle when `sortable` is true. */
  rowId: string;
  columns: readonly TimeColumn[];
  /** Content of the fixed `w-52` label cell (name / code / overall %). */
  label: React.ReactNode;
  /** The absolutely-positioned bars layer. */
  children?: React.ReactNode;
  /** Track min-height in px (Spaces grows this for stacked overlaps). Default 52. */
  minHeight?: number;
  /** Per-column off-time predicate (Spaces tints resource-specific off-time). */
  isOffTime?: (col: TimeColumn) => boolean;
  /** Ref attached to the time-track element (Spaces uses it as the row droppable). */
  trackRef?: (node: HTMLElement | null) => void;
  /** Extra classes on the time-track (Spaces tints it on drag-over). */
  trackClassName?: string;
  /** When true, the row is draggable for reordering (Spaces). */
  sortable?: boolean;
  /** dnd-kit `data` payload for the sortable (consumed by the page's onDragEnd). */
  sortableData?: Record<string, unknown>;
  testId?: string;
}

function DefaultColumnCells({
  columns,
  isOffTime,
}: {
  columns: readonly TimeColumn[];
  isOffTime?: (col: TimeColumn) => boolean;
}) {
  return (
    <>
      {columns.map((col) => {
        // Resource-specific off-time gets the same destructive tint as weekends/
        // global off-time; otherwise fall back to the shared column tint.
        const tint = isOffTime?.(col) ? OFFTIME_TINT_CLASS : columnTintClass(col);
        // The off-time background column carries the diagonal hatch (the "Off"
        // segment bars stay hatch-free so the two don't double up). Outside
        // working hours (muted) is not a problem state, so it stays hatch-free.
        const hatch = tint === OFFTIME_TINT_CLASS ? PROBLEM_HATCH_CLASS : "";
        return (
          <div
            key={col.start.getTime()}
            className={`flex-1 min-w-[60px] border-r ${tint} ${hatch}`}
          />
        );
      })}
    </>
  );
}

function RowInner({
  columns,
  label,
  children,
  minHeight = 52,
  isOffTime,
  trackRef,
  trackClassName,
  testId,
  dragRef,
  dragStyle,
}: TimelineRowProps & {
  dragRef?: (node: HTMLElement | null) => void;
  dragStyle?: React.CSSProperties;
}) {
  return (
    <div
      ref={dragRef}
      style={dragStyle}
      className="flex border-b hover:bg-accent/30 transition-colors motion-reduce:transition-none"
      data-testid={testId}
    >
      <div className="w-52 flex-shrink-0 px-3 py-2 border-r flex items-center gap-2">
        {label}
      </div>
      <div
        ref={trackRef}
        className={`flex-1 flex relative ${trackClassName ?? ""}`}
        style={{ minHeight: `${minHeight}px` }}
      >
        <DefaultColumnCells columns={columns} isOffTime={isOffTime} />
        {children}
      </div>
    </div>
  );
}

function SortableRow(props: TimelineRowProps) {
  const { setNodeRef, transform, transition, isDragging } = useSortable({
    id: props.rowId,
    data: props.sortableData ?? { type: "grid-row" },
  });
  const dragStyle: React.CSSProperties = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : 1,
  };
  return <RowInner {...props} dragRef={setNodeRef} dragStyle={dragStyle} />;
}

export const TimelineRow = React.memo(function TimelineRow(props: TimelineRowProps) {
  if (props.sortable) return <SortableRow {...props} />;
  return <RowInner {...props} />;
});
