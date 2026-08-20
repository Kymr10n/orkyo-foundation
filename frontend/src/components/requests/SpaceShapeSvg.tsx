import { memo } from "react";
import type { Coordinate, ResourceGeometry } from "@foundation/src/types/geometry";
import { cn } from "@foundation/src/lib/utils";
import {
  SPACE_CANVAS_DEFAULT,
  SPACE_CANVAS_DRAGGING,
  SPACE_CANVAS_LABEL,
  SPACE_CANVAS_SELECTED,
} from "@foundation/src/components/utilization/schedule-colors";

interface SpaceShapeSvgProps {
  // Nullable to match the wire shape, where an absent code or geometry is null.
  space: { id: string; name: string; code?: string | null; geometry?: ResourceGeometry | null };
  isDragging?: boolean;
  editEnabled?: boolean;
  selectedResourceId?: string;
  /**
   * Non-null ONLY for the shape currently being resized — carries the live handle
   * index and pointer position. Every other shape receives `null`, so the live
   * pointer no longer flows to untouched shapes and they skip re-rendering during
   * a resize/drag gesture (this component is memoized).
   */
  resizePreview?: { handleIndex: number; mousePosition: Coordinate } | null;
  spaceColors?: Record<string, { fill: string; stroke: string }>;
}

export const SpaceShapeSvg = memo(function SpaceShapeSvg({
  space,
  isDragging = false,
  editEnabled = false,
  selectedResourceId,
  resizePreview,
  spaceColors,
}: SpaceShapeSvgProps) {
  if (!space.geometry) return null;

  const isSelected = selectedResourceId === space.id;
  const showResizeHandles = editEnabled && isSelected && !isDragging;
  // Shapes stay pointer-event-enabled even in view mode so a double-click still
  // registers; the cursor signals whether spatial gestures are available.
  const shapeClassName = isDragging
    ? "pointer-events-none"
    : cn("hover:opacity-80", editEnabled ? "cursor-move" : "cursor-pointer");

  // Occupancy colours when the caller has them (the utilization floorplan), otherwise the shared
  // default — which is what the Floorplan management page always gets, since a station there has
  // no status to show. All four live beside the calendar and grid tokens so the surfaces cannot
  // drift apart.
  const colors = isDragging
    ? SPACE_CANVAS_DRAGGING
    : isSelected
      ? SPACE_CANVAS_SELECTED
      : spaceColors?.[space.id] ?? SPACE_CANVAS_DEFAULT;
  const { fill: fillColor, stroke: strokeColor } = colors;
  const strokeDasharray = isDragging ? "5,5" : undefined;

  if (space.geometry.type === "rectangle") {
    const [start, end] = resizePreview
      ? [
          resizePreview.handleIndex === 0
            ? resizePreview.mousePosition
            : space.geometry.coordinates[0],
          resizePreview.handleIndex === 1
            ? resizePreview.mousePosition
            : space.geometry.coordinates[1],
        ]
      : space.geometry.coordinates;

    const x = Math.min(start.x, end.x);
    const y = Math.min(start.y, end.y);
    const width = Math.abs(end.x - start.x);
    const height = Math.abs(end.y - start.y);

    return (
      <g
        key={space.id}
        data-space-id={space.id}
        className={shapeClassName}
      >
        <rect
          x={x}
          y={y}
          width={width}
          height={height}
          fill={fillColor}
          stroke={strokeColor}
          strokeWidth="2"
          strokeDasharray={strokeDasharray}
        />
        <text
          x={x + width / 2}
          y={y + height / 2}
          textAnchor="middle"
          dominantBaseline="middle"
          className="text-xs font-medium pointer-events-none"
          fill={SPACE_CANVAS_LABEL}
        >
          {space.code || space.name}
        </text>

        {/* Resize handles for rectangles — geometry stores [start, end] so only
            the two diagonal corners (indices 0 and 1) have independent control. */}
        {showResizeHandles && (
          <>
            {/* Top-left corner (index 0 = start) */}
            <circle
              cx={x}
              cy={y}
              r="6"
              fill="#3b82f6"
              stroke="white"
              strokeWidth="2"
              className="cursor-nwse-resize"
              data-resize-handle="true"
              data-space-id={space.id}
              data-handle-index="0"
            />
            {/* Bottom-right corner (index 1 = end) */}
            <circle
              cx={x + width}
              cy={y + height}
              r="6"
              fill="#3b82f6"
              stroke="white"
              strokeWidth="2"
              className="cursor-nwse-resize"
              data-resize-handle="true"
              data-space-id={space.id}
              data-handle-index="1"
            />
          </>
        )}
      </g>
    );
  } else if (space.geometry.type === "polygon") {
    const coordinates = resizePreview
      ? space.geometry.coordinates.map((coord, i) =>
          i === resizePreview.handleIndex ? resizePreview.mousePosition : coord,
        )
      : space.geometry.coordinates;

    const pathData =
      coordinates
        .map((p, i) => `${i === 0 ? "M" : "L"} ${p.x} ${p.y}`)
        .join(" ") + " Z";

    const centroid = coordinates.reduce(
      (acc, p) => ({ x: acc.x + p.x, y: acc.y + p.y }),
      { x: 0, y: 0 },
    );
    centroid.x /= coordinates.length;
    centroid.y /= coordinates.length;

    return (
      <g
        key={space.id}
        data-space-id={space.id}
        className={shapeClassName}
      >
        <path
          d={pathData}
          fill={fillColor}
          stroke={strokeColor}
          strokeWidth="2"
          strokeDasharray={strokeDasharray}
        />
        <text
          x={centroid.x}
          y={centroid.y}
          textAnchor="middle"
          dominantBaseline="middle"
          className="text-xs font-medium pointer-events-none"
          fill={SPACE_CANVAS_LABEL}
        >
          {space.code || space.name}
        </text>

        {/* Resize handles for polygons - vertex handles */}
        {showResizeHandles &&
          coordinates.map((coord, i) => (
            <circle
              key={i}
              cx={coord.x}
              cy={coord.y}
              r="6"
              fill="#3b82f6"
              stroke="white"
              strokeWidth="2"
              className="cursor-move"
              data-resize-handle="true"
              data-space-id={space.id}
              data-handle-index={i}
            />
          ))}
      </g>
    );
  } else if (space.geometry.type === "circle") {
    // Stored as [centre, rimPoint]. The centre is never substituted during a resize — dragging the
    // rim changes the radius and nothing else, so the shape cannot drift while being resized.
    const [centre, rim] = space.geometry.coordinates;
    const livePoint = resizePreview?.handleIndex === 1 ? resizePreview.mousePosition : rim;
    const radius = Math.hypot(livePoint.x - centre.x, livePoint.y - centre.y);

    return (
      <g key={space.id} data-space-id={space.id} className={shapeClassName}>
        <circle
          cx={centre.x}
          cy={centre.y}
          r={radius}
          fill={fillColor}
          stroke={strokeColor}
          strokeWidth="2"
          strokeDasharray={strokeDasharray}
        />
        <text
          x={centre.x}
          y={centre.y}
          textAnchor="middle"
          dominantBaseline="middle"
          className="text-xs font-medium pointer-events-none"
          fill={SPACE_CANVAS_LABEL}
        >
          {space.code || space.name}
        </text>

        {/* One handle, sitting on the rim where the pointer left it rather than at a fixed
            bearing, so the grab point stays under the cursor through the drag. */}
        {showResizeHandles && (
          <circle
            cx={livePoint.x}
            cy={livePoint.y}
            r="6"
            fill="#3b82f6"
            stroke="white"
            strokeWidth="2"
            className="cursor-nwse-resize"
            data-resize-handle="true"
            data-space-id={space.id}
            data-handle-index="1"
          />
        )}
      </g>
    );
  }

  return null;
});
