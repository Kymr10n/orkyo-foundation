import { useState, type CSSProperties } from "react";
import { createPortal } from "react-dom";
import { useDndMonitor, type DragMoveEvent } from "@dnd-kit/core";
import { resolveColumnIndex } from "./time-grid-utils";

/**
 * Live "where will it land" hint for the Spaces grid.
 *
 * The whole row is a single droppable (`SpaceRow` → `track-<spaceId>`), so the
 * old per-cell `isOver` tint is gone. Rather than re-introduce per-cell
 * droppables or re-render the grid on every pointer move, this component
 * subscribes to dnd-kit drag events in isolation and renders ONE fixed-position
 * highlight over the column the pointer is on. Only this component re-renders
 * during a drag — never the grid.
 *
 * Must be mounted inside the `DndContext`.
 */
// Positioning subset applied directly via `style`; typed from CSSProperties so it
// stays assignable as React's CSSProperties typings tighten across versions.
type Highlight = Pick<CSSProperties, "left" | "top" | "width" | "height">;

export function DropColumnIndicator() {
  const [highlight, setHighlight] = useState<Highlight | null>(null);

  const update = (event: DragMoveEvent) => {
    const over = event.over;
    const data = over?.data.current as
      | { type?: string; columnStartsMs?: number[] }
      | undefined;
    if (!over || data?.type !== "space-track" || !data.columnStartsMs?.length) {
      setHighlight(null);
      return;
    }
    const { rect } = over;
    const count = data.columnStartsMs.length;
    const columnWidth = rect.width / count;
    const activator = event.activatorEvent as PointerEvent;
    const pointerX = activator.clientX + event.delta.x;
    const idx = resolveColumnIndex(pointerX, rect.left, rect.width, count);
    setHighlight({
      left: rect.left + idx * columnWidth,
      top: rect.top,
      width: columnWidth,
      height: rect.height,
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
    <div
      className="pointer-events-none fixed z-50 rounded-sm border-2 border-blue-500/70 bg-blue-500/20"
      style={highlight}
    />,
    document.body,
  );
}
