import type { Coordinate, DrawingMode, SpaceGeometry } from "@foundation/src/types/space";

interface SpaceShapeSvgProps {
  space: { id: string; name: string; code?: string; geometry?: SpaceGeometry };
  isDragging?: boolean;
  drawingMode: DrawingMode;
  selectedSpaceId?: string;
  resizingSpace: { id: string; handleIndex: number; geometry: SpaceGeometry } | null;
  mousePosition: Coordinate | null;
  spaceColors?: Record<string, { fill: string; stroke: string }>;
}

export function SpaceShapeSvg({
  space,
  isDragging = false,
  drawingMode,
  selectedSpaceId,
  resizingSpace,
  mousePosition,
  spaceColors,
}: SpaceShapeSvgProps) {
  if (!space.geometry) return null;

  const isSelected = selectedSpaceId === space.id;
  const showResizeHandles = drawingMode === "resize" && isSelected;

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
    const [start, end] =
      resizingSpace?.id === space.id && mousePosition
        ? [
            resizingSpace.handleIndex === 0
              ? mousePosition
              : resizingSpace.geometry.coordinates[0],
            resizingSpace.handleIndex === 1
              ? mousePosition
              : resizingSpace.geometry.coordinates[1],
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
        className={
          isDragging
            ? "pointer-events-none"
            : "cursor-pointer hover:opacity-80"
        }
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
    const coordinates =
      resizingSpace?.id === space.id && mousePosition
        ? resizingSpace.geometry.coordinates.map((coord, i) =>
            i === resizingSpace.handleIndex ? mousePosition : coord,
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
        className={
          isDragging
            ? "pointer-events-none"
            : "cursor-pointer hover:opacity-80"
        }
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
}
