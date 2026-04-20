import type { Coordinate, DrawingMode } from "@/types/space";

interface CanvasInstructionsProps {
  isPassiveMode: boolean;
  drawingMode: DrawingMode;
  drawingPoints: Coordinate[];
  selectedSpaceId?: string;
}

export function CanvasInstructions({
  isPassiveMode,
  drawingMode,
  drawingPoints,
  selectedSpaceId,
}: CanvasInstructionsProps) {
  const showDrawing = !isPassiveMode;
  const showResize = drawingMode === "resize" && selectedSpaceId;

  if (!showDrawing && !showResize) return null;

  return (
    <>
      {showDrawing && (
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
      )}

      {showResize && (
        <div className="absolute top-4 left-4 bg-background/95 border border-border rounded-lg px-3 py-2 text-sm shadow-lg pointer-events-none">
          <p>Drag the handles to resize the space</p>
          <p className="text-xs text-muted-foreground mt-1">
            Click a space to select it
          </p>
        </div>
      )}
    </>
  );
}
