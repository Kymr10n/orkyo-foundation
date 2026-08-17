import type { Coordinate, DrawingMode } from "@foundation/src/types/geometry";

interface DrawingPreviewSvgProps {
  drawingMode: DrawingMode;
  drawingPoints: Coordinate[];
  mousePosition: Coordinate | null;
}

export function DrawingPreviewSvg({
  drawingMode,
  drawingPoints,
  mousePosition,
}: DrawingPreviewSvgProps) {
  const rectanglePreview =
    drawingMode === "rectangle" &&
    drawingPoints.length === 1 &&
    mousePosition ? (() => {
      const [start] = drawingPoints;
      const end = mousePosition;

      const x = Math.min(start.x, end.x);
      const y = Math.min(start.y, end.y);
      const width = Math.abs(end.x - start.x);
      const height = Math.abs(end.y - start.y);

      return (
        <rect
          x={x}
          y={y}
          width={width}
          height={height}
          fill="rgba(59, 130, 246, 0.2)"
          stroke="#3b82f6"
          strokeWidth="2"
          strokeDasharray="4 4"
          pointerEvents="none"
        />
      );
    })() : null;

  const polygonPreview =
    drawingMode === "polygon" && drawingPoints.length > 0 ? (() => {
      const allPoints = mousePosition
        ? [...drawingPoints, mousePosition]
        : drawingPoints;
      const pathData = allPoints
        .map((p, i) => `${i === 0 ? "M" : "L"} ${p.x} ${p.y}`)
        .join(" ");

      return (
        <>
          {/* Polygon path */}
          <path
            d={pathData}
            fill="rgba(59, 130, 246, 0.2)"
            stroke="#3b82f6"
            strokeWidth="2"
            strokeDasharray="4 4"
            pointerEvents="none"
          />
          {/* Points */}
          {drawingPoints.map((point, i) => (
            <circle
              key={i}
              cx={point.x}
              cy={point.y}
              r="4"
              fill="#3b82f6"
              pointerEvents="none"
            />
          ))}
        </>
      );
    })() : null;

  const circlePreview =
    drawingMode === "circle" &&
    drawingPoints.length === 1 &&
    mousePosition ? (() => {
      const [centre] = drawingPoints;
      const radius = Math.hypot(mousePosition.x - centre.x, mousePosition.y - centre.y);
      return (
        <>
          <circle
            cx={centre.x}
            cy={centre.y}
            r={radius}
            fill="rgba(59, 130, 246, 0.1)"
            stroke="#3b82f6"
            strokeWidth="2"
            strokeDasharray="5,5"
          />
          {/* The radius line is what says which point is anchored — without it a growing circle
              reads as ambiguous about whether the centre or the edge is being placed. */}
          <line
            x1={centre.x}
            y1={centre.y}
            x2={mousePosition.x}
            y2={mousePosition.y}
            stroke="#3b82f6"
            strokeWidth="1"
            strokeDasharray="3,3"
          />
          <circle cx={centre.x} cy={centre.y} r="4" fill="#3b82f6" />
        </>
      );
    })() : null;

  return (
    <>
      {rectanglePreview}
      {polygonPreview}
      {circlePreview}
    </>
  );
}
