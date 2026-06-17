import type { Coordinate, DrawingMode } from "@foundation/src/types/space";

interface CanvasInstructionsProps {
  isPassiveMode: boolean;
  drawingMode: DrawingMode;
  drawingPoints: Coordinate[];
}

export function CanvasInstructions({
  isPassiveMode,
  drawingMode,
  drawingPoints,
}: CanvasInstructionsProps) {
  const showDrawing = !isPassiveMode;

  if (!showDrawing) return null;

  return (
    <div className="absolute top-4 left-4 bg-background/95 border border-border rounded-lg px-3 py-2 text-sm shadow-lg pointer-events-none">
      {drawingMode === "rectangle" && (
        <p>
          {drawingPoints.length === 0
            ? "Click to place first corner"
            : "Click to place opposite corner"}
        </p>
      )}
      {drawingMode === "polygon" && (
        <p>
          {drawingPoints.length < 3
            ? `Click to add points (${drawingPoints.length}/3 minimum)`
            : "Double-click to complete polygon"}
        </p>
      )}
      <p className="text-xs text-muted-foreground mt-1">
        Press ESC to cancel
      </p>
    </div>
  );
}
