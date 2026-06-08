import React from "react";
import { useSortable } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import type { TimeColumn } from "./scheduler-types";

/**
 * Shared row chrome for both utilization grids (Spaces + People).
 *
 * Owns the `w-52` label cell, the `flex-1 flex relative` time track, and the
 * per-column gridline + off-day tint cells. The actual allocation bars
 * (`ScheduledRequestOverlay` for Spaces, `PersonSegmentBar` for People) are
 * passed as `children` and rendered absolutely on top of the column cells.
 *
 * This centralises the gridline/tint layer that previously lived in two
 * places (`TimeCell` for Spaces, inline in `PersonTimelineRow` for People) and
 * guarantees the body columns line up with the shared header.
 */

/** Tailwind tint for a column cell — single source for both grids. */
export function columnTintClass(col: TimeColumn): string {
  if (col.isWeekend || col.isGlobalOffTime) return "bg-destructive/15";
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
  /**
   * Override the per-column cell (Spaces supplies a droppable `TimeCell`).
   * Default renders a plain gridline + off-day tint div.
   */
  renderColumnCell?: (col: TimeColumn) => React.ReactNode;
  /** When true, the row is draggable for reordering (Spaces). */
  sortable?: boolean;
  /** dnd-kit `data` payload for the sortable (consumed by the page's onDragEnd). */
  sortableData?: Record<string, unknown>;
  testId?: string;
}

function DefaultColumnCells({ columns }: { columns: readonly TimeColumn[] }) {
  return (
    <>
      {columns.map((col) => (
        <div
          key={col.start.getTime()}
          className={`flex-1 min-w-[60px] border-r ${columnTintClass(col)}`}
        />
      ))}
    </>
  );
}

function RowInner({
  columns,
  label,
  children,
  minHeight = 52,
  renderColumnCell,
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
      className="flex border-b hover:bg-accent/30 transition-colors"
      data-testid={testId}
    >
      <div className="w-52 flex-shrink-0 px-3 py-2 border-r flex items-center gap-2">
        {label}
      </div>
      <div className="flex-1 flex relative" style={{ minHeight: `${minHeight}px` }}>
        {renderColumnCell ? (
          columns.map((col) => (
            <React.Fragment key={col.start.getTime()}>
              {renderColumnCell(col)}
            </React.Fragment>
          ))
        ) : (
          <DefaultColumnCells columns={columns} />
        )}
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
