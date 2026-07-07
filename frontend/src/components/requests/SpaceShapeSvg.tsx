import { memo } from "react";
import type { Coordinate, SpaceGeometry } from "@foundation/src/types/space";
import { cn } from "@foundation/src/lib/utils";

interface SpaceShapeSvgProps {
  space: { id: string; name: string; code?: string; geometry?: SpaceGeometry };
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

  const customColors = spaceColors?.[space.id];
  const fillColor = isDragging
    ? "rgba(59, 130, 246, 0.5)"
    : isSelected
      ? "rgba(59, 130, 246, 0.3)"
      : customColors?.fill ?? "rgba(148, 163, 184, 0.2)";
  const strokeColor = isDragging
    ? "#2563eb"
    : isSelected
      ? "#3b82f6"
      : customColors?.stroke ?? "#94a3b8";
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
          fill="#1e293b"
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
          fill="#1e293b"
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
  }

  return null;
});
