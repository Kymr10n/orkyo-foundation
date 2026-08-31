import { useState } from "react";
import { createPortal } from "react-dom";
import { useDndMonitor, type DragMoveEvent } from "@dnd-kit/core";
import { formatDropInstant, resolveDropStartMs, viewPositionPercent } from "./time-grid-utils";
import type { TimeScale } from "./ScaleSelect";

/**
 * Live "where will it land" hint for the Spaces grid.
 *
 * The whole row is a single droppable (`SpaceRow` → `track-<spaceId>`), so the
 * old per-cell `isOver` tint is gone. Rather than re-introduce per-cell
 * droppables or re-render the grid on every pointer move, this component
 * subscribes to dnd-kit drag events in isolation and renders ONE fixed-position
 * marker at the exact instant the bar will land on, labelled with that instant.
 * Only this component re-renders during a drag — never the grid.
 *
 * The marker tracks the landing position continuously, through the same
 * resolver the drop handler uses, so it can never promise a slot the drop then
 * does not honour. Its value is the target *row*: the dragged bar floats freely
 * under the pointer and can hover between rows, while the marker is drawn in the
 * row that will actually receive it.
 *
 * Deliberately a slim line, not a filled column box. The dragged bar itself
 * already glides continuously under the pointer (dnd-kit applies its transform
 * straight to that node). A solid full-day box painted above it snapped from
 * column to column on every move, so the eye read the whole drag as quantized
 * even though the bar was not.
 *
 * Must be mounted inside the `DndContext`.
 */
/** Viewport coordinates of the marker, plus the instant it names. */
interface Highlight {
  left: number;
  top: number;
  height: number;
  label: string;
}

/** Wide enough to see against a busy row, narrow enough not to read as a block. */
const MARKER_WIDTH_PX = 3;

/** Breathing room between the pill's baseline edge and the top of the line. */
const LABEL_GAP_PX = 4;

export function DropPositionIndicator({ scale }: { scale: TimeScale }) {
  const [highlight, setHighlight] = useState<Highlight | null>(null);

  const update = (event: DragMoveEvent) => {
    const over = event.over;
    const data = over?.data.current as
      | { type?: string; viewStartMs?: number; viewEndMs?: number }
      | undefined;
    const dragged = event.active.data.current as
      | { startTs?: string | null; endTs?: string | null }
      | undefined;
    if (
      !over || data?.type !== "space-track" || data.viewStartMs === undefined ||
      data.viewEndMs === undefined || !dragged?.startTs || !dragged.endTs
    ) {
      setHighlight(null);
      return;
    }

    const { rect } = over;
    const viewStartMs = data.viewStartMs;
    const origStartMs = new Date(dragged.startTs).getTime();
    const startMs = resolveDropStartMs(
      origStartMs,
      new Date(dragged.endTs).getTime() - origStartMs,
      event.delta.x,
      rect.width,
      viewStartMs,
      data.viewEndMs,
    );

    // Outside the window (a bar that already started before it) — no honest place
    // to draw the marker, so draw none rather than pin it to an edge.
    const percent = viewPositionPercent(startMs, viewStartMs, data.viewEndMs);
    if (percent === null) {
      setHighlight(null);
      return;
    }

    setHighlight({
      left: rect.left + (percent / 100) * rect.width,
      top: rect.top,
      height: rect.height,
      label: formatDropInstant(new Date(startMs), scale),
    });
  };

  useDndMonitor({
    onDragMove: update,
    onDragOver: update,
    onDragEnd: () => setHighlight(null),
    onDragCancel: () => setHighlight(null),
  });

  if (!highlight) return null;
  return createPortal(
    <>
      <div
        className="pointer-events-none fixed z-50 rounded-full bg-blue-500"
        style={{ left: highlight.left, top: highlight.top, width: MARKER_WIDTH_PX, height: highlight.height }}
      />
      {/* Centred on the line and lifted clear of it. The track starts after the row-label
          column and ends before the page gutter, so a centred pill has room at both ends. */}
      <div
        className="pointer-events-none fixed z-50 -translate-x-1/2 -translate-y-full whitespace-nowrap rounded bg-blue-500 px-1.5 py-0.5 text-[11px] font-medium leading-tight text-white shadow-sm tabular-nums"
        style={{ left: highlight.left, top: highlight.top - LABEL_GAP_PX }}
      >
        {highlight.label}
      </div>
    </>,
    document.body,
  );
}
