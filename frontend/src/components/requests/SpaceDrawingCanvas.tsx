/**
 * SpaceDrawingCanvas - Interactive canvas for drawing spaces on a floorplan
 *
 * Features:
 * - Rectangle drawing (2 clicks: top-left, bottom-right)
 * - Polygon drawing (multiple clicks, double-click to complete)
 * - Visual feedback during drawing
 * - Coordinate transformation (screen to floorplan coordinates)
 */

import { cn } from "@/lib/utils";
import type { Coordinate, DrawingMode, SpaceGeometry } from "@/types/space";
import {
  type MouseEvent as ReactMouseEvent,
  useEffect,
  useRef,
  useState,
  useCallback,
} from "react";
import { SpaceShapeSvg } from "./SpaceShapeSvg";
import { DrawingPreviewSvg } from "./DrawingPreviewSvg";
import { CanvasInstructions } from "./CanvasInstructions";
interface SpaceDrawingCanvasProps {
  /** Floorplan image URL */
  floorplanUrl?: string;
  /** Floorplan dimensions in pixels */
  floorplanDimensions?: { width: number; height: number };
  /** Current drawing mode */
  drawingMode: DrawingMode;
  /** Callback when drawing is complete */
  onDrawingComplete: (geometry: SpaceGeometry) => void;
  /** Callback when drawing is cancelled */
  onDrawingCancel: () => void;
  /** Existing spaces to display */
  existingSpaces?: {
    id: string;
    name: string;
    code?: string;
    geometry?: SpaceGeometry;
  }[];
  /** Selected space ID */
  selectedSpaceId?: string;
  /** Callback when a space is clicked */
  onSpaceClick?: (spaceId: string) => void;
  /** Callback when a space is moved */
  onSpaceMove?: (spaceId: string, newGeometry: SpaceGeometry) => void;
  /** Callback when a space is resized */
  onSpaceResize?: (spaceId: string, newGeometry: SpaceGeometry) => void;
  /** Current zoom level */
  zoom?: number;
  /** Custom colors per space ID - { fill, stroke } */
  spaceColors?: Record<string, { fill: string; stroke: string }>;
  /** How to scale to fit: 'width' (scroll vertically), 'contain' (fit both) */
  fitMode?: 'width' | 'contain';
  /** Class name for styling */
  className?: string;
}

export function SpaceDrawingCanvas({
  floorplanUrl,
  floorplanDimensions,
  drawingMode,
  onDrawingComplete,
  onDrawingCancel,
  existingSpaces = [],
  selectedSpaceId,
  onSpaceClick,
  onSpaceMove,
  onSpaceResize,
  zoom = 1,
  spaceColors,
  fitMode = 'width',
  className,
}: SpaceDrawingCanvasProps) {
  const [drawingPoints, setDrawingPoints] = useState<Coordinate[]>([]);
  const [mousePosition, setMousePosition] = useState<Coordinate | null>(null);
  // True when not actively drawing — used to guard several event handlers.
  const isPassiveMode =
    drawingMode === "none" || drawingMode === "select" || drawingMode === "resize";

  const [draggingSpace, setDraggingSpace] = useState<{
    id: string;
    startPos: Coordinate;
    geometry: SpaceGeometry;
  } | null>(null);
  const [resizingSpace, setResizingSpace] = useState<{
    id: string;
    handleIndex: number;
    geometry: SpaceGeometry;
  } | null>(null);
  const [baseScale, setBaseScale] = useState<number | null>(null);
  const baseScaleRef = useRef<number | null>(null);
  const canvasRef = useRef<HTMLDivElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);

  // Calculate base scale to fit container
  const updateBaseScale = useCallback(() => {
    if (!containerRef.current || !floorplanDimensions) return;
    
    const containerWidth = containerRef.current.clientWidth - 32; // Account for padding
    const containerHeight = containerRef.current.clientHeight - 32;
    const imageWidth = floorplanDimensions.width;
    const imageHeight = floorplanDimensions.height;
    
    if (imageWidth <= 0 || containerWidth <= 50) return;
    
    let newScale: number;
    if (fitMode === 'contain' && imageHeight > 0 && containerHeight > 50) {
      // Scale to fit both width and height (for fixed-height containers)
      const scaleX = containerWidth / imageWidth;
      const scaleY = containerHeight / imageHeight;
      newScale = Math.min(scaleX, scaleY, 1);
    } else {
      // Scale to fit width only (scrollable containers)
      newScale = Math.min(containerWidth / imageWidth, 1);
    }
    
    // Only update if significantly different to avoid layout thrashing
    if (baseScaleRef.current === null || Math.abs(newScale - baseScaleRef.current) > 0.01) {
      baseScaleRef.current = newScale;
      setBaseScale(newScale);
    }
  }, [floorplanDimensions, fitMode]);

  // Update base scale on mount and resize
  useEffect(() => {
    updateBaseScale();
    
    const resizeObserver = new ResizeObserver(updateBaseScale);
    if (containerRef.current) {
      resizeObserver.observe(containerRef.current);
    }
    
    return () => resizeObserver.disconnect();
  }, [updateBaseScale]);

  // Reset drawing when mode changes
  useEffect(() => {
    if (isPassiveMode) {
      setDrawingPoints([]);
      setMousePosition(null);
    }
  }, [drawingMode]); // eslint-disable-line react-hooks/exhaustive-deps

  // Convert screen coordinates to canvas coordinates (accounting for zoom and offset)
  const screenToCanvas = (screenX: number, screenY: number): Coordinate => {
    if (!canvasRef.current) return { x: screenX, y: screenY };

    const rect = canvasRef.current.getBoundingClientRect();
    const effectiveScale = (baseScale ?? 1) * zoom;

    // Convert screen coordinates to canvas coordinates, accounting for scale
    return {
      x: (screenX - rect.left) / effectiveScale,
      y: (screenY - rect.top) / effectiveScale,
    };
  };

  const handleMouseMove = (e: ReactMouseEvent<HTMLDivElement>) => {
    const pos = screenToCanvas(e.clientX, e.clientY);

    // For resize/drag/draw previews, mousePosition drives the render — just update it.
    if (resizingSpace || draggingSpace || !isPassiveMode) {
      setMousePosition(pos);
    }
  };

  const handleMouseDown = (e: ReactMouseEvent<HTMLDivElement>) => {
    // Check for resize handle click
    if (drawingMode === "resize") {
      const target = e.target as HTMLElement;
      const handleElement = target.closest("[data-resize-handle]");

      if (handleElement) {
        e.preventDefault();
        const spaceId = handleElement.getAttribute("data-space-id");
        const handleIndex = parseInt(
          handleElement.getAttribute("data-handle-index") || "0",
        );
        const space = existingSpaces.find((s) => s.id === spaceId);

        if (space?.geometry && onSpaceResize) {
          const pos = screenToCanvas(e.clientX, e.clientY);
          setMousePosition(pos);
          setResizingSpace({
            id: space.id,
            handleIndex,
            geometry: space.geometry,
          });
        }
        return;
      }
    }

    // Only allow dragging in select mode
    if (drawingMode !== "select") return;

    const target = e.target as HTMLElement;
    const spaceElement = target.closest("[data-space-id]");

    if (spaceElement) {
      e.preventDefault();
      const spaceId = spaceElement.getAttribute("data-space-id");
      const space = existingSpaces.find((s) => s.id === spaceId);

      if (space?.geometry && onSpaceMove) {
        const pos = screenToCanvas(e.clientX, e.clientY);
        setDraggingSpace({
          id: space.id,
          startPos: pos,
          geometry: space.geometry,
        });
      }
    }
  };

  const handleMouseUp = (e: ReactMouseEvent<HTMLDivElement>) => {
    // Handle resize completion
    if (resizingSpace && onSpaceResize && mousePosition) {
      const space = existingSpaces.find((s) => s.id === resizingSpace.id);
      if (!space?.geometry) {
        setResizingSpace(null);
        setMousePosition(null);
        return;
      }

      if (space.geometry.type === "rectangle") {
        // For rectangles, create new geometry with updated corner
        const newCoords = [...resizingSpace.geometry.coordinates];
        newCoords[resizingSpace.handleIndex] = mousePosition;

        const newGeometry: SpaceGeometry = {
          type: "rectangle",
          coordinates: newCoords,
        };

        onSpaceResize(resizingSpace.id, newGeometry);
      } else if (space.geometry.type === "polygon") {
        // For polygons, create new geometry with updated vertex
        const newCoords = [...resizingSpace.geometry.coordinates];
        newCoords[resizingSpace.handleIndex] = mousePosition;

        const newGeometry: SpaceGeometry = {
          type: "polygon",
          coordinates: newCoords,
        };

        onSpaceResize(resizingSpace.id, newGeometry);
      }

      setResizingSpace(null);
      setMousePosition(null);
      return;
    }

    if (draggingSpace && onSpaceMove) {
      const pos = screenToCanvas(e.clientX, e.clientY);
      const deltaX = pos.x - draggingSpace.startPos.x;
      const deltaY = pos.y - draggingSpace.startPos.y;

      // Only save if actually moved
      if (Math.abs(deltaX) > 1 || Math.abs(deltaY) > 1) {
        const newGeometry: SpaceGeometry = {
          type: draggingSpace.geometry.type,
          coordinates: draggingSpace.geometry.coordinates.map((coord) => ({
            x: coord.x + deltaX,
            y: coord.y + deltaY,
          })),
        };

        onSpaceMove(draggingSpace.id, newGeometry);
      }

      setDraggingSpace(null);
    }
  };

  const handleMouseLeave = () => {
    setMousePosition(null);
    // Cancel drag if mouse leaves canvas
    if (draggingSpace) {
      setDraggingSpace(null);
    }
    // Cancel resize if mouse leaves canvas
    if (resizingSpace) {
      setResizingSpace(null);
    }
  };

  const handleClick = (e: ReactMouseEvent<HTMLDivElement>) => {
    // Check if clicking on an existing space
    if (isPassiveMode) {
      const target = e.target as HTMLElement;
      const spaceElement = target.closest("[data-space-id]");
      if (spaceElement && onSpaceClick) {
        const spaceId = spaceElement.getAttribute("data-space-id");
        if (spaceId) {
          onSpaceClick(spaceId);
          return;
        }
      }
    }

    if (isPassiveMode) return;

    const point = screenToCanvas(e.clientX, e.clientY);

    if (drawingMode === "rectangle") {
      if (drawingPoints.length === 0) {
        // First point
        setDrawingPoints([point]);
      } else if (drawingPoints.length === 1) {
        // Second point - complete rectangle
        const geometry: SpaceGeometry = {
          type: "rectangle",
          coordinates: [drawingPoints[0], point],
        };
        onDrawingComplete(geometry);
        setDrawingPoints([]);
        setMousePosition(null);
      }
    } else if (drawingMode === "polygon") {
      setDrawingPoints([...drawingPoints, point]);
    }
  };

  const handleDoubleClick = (e: ReactMouseEvent<HTMLDivElement>) => {
    if (drawingMode === "polygon" && drawingPoints.length >= 3) {
      e.preventDefault();
      // Complete polygon (don't add the double-click point)
      const geometry: SpaceGeometry = {
        type: "polygon",
        coordinates: drawingPoints,
      };
      onDrawingComplete(geometry);
      setDrawingPoints([]);
      setMousePosition(null);
    }
  };

  const handleKeyDown = useCallback((e: KeyboardEvent) => {
    if (
      e.key === "Escape" &&
      (drawingMode === "rectangle" || drawingMode === "polygon")
    ) {
      onDrawingCancel();
      setDrawingPoints([]);
      setMousePosition(null);
    }
  }, [drawingMode, onDrawingCancel]);

  useEffect(() => {
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [handleKeyDown]);

  const canvasWidth = floorplanDimensions?.width || 800;
  const canvasHeight = floorplanDimensions?.height || 600;
  const effectiveScale = (baseScale ?? 1) * zoom;
  const isScaleReady = baseScale !== null;

  function getCanvasCursor(): string {
    if (resizingSpace || draggingSpace) return "grabbing";
    if (drawingMode === "none") return "default";
    if (drawingMode === "select") return "grab";
    if (drawingMode === "resize") return "pointer";
    return "crosshair";
  }

  return (
    <div ref={containerRef} className={cn("relative overflow-auto bg-muted/30", className)}>
      {/* Wrapper for proper scroll area sizing - hidden until scale is calculated */}
      <div 
        style={{ 
          width: isScaleReady ? `${canvasWidth * effectiveScale}px` : '100%',
          height: isScaleReady ? `${canvasHeight * effectiveScale}px` : '100%',
          position: 'relative',
          opacity: isScaleReady ? 1 : 0,
          transition: 'opacity 0.15s ease-in-out',
        }}
      >
        {/* Scaled container */}
        <div
          ref={canvasRef}
          style={{
            transform: `scale(${effectiveScale})`,
            transformOrigin: "top left",
            width: `${canvasWidth}px`,
            height: `${canvasHeight}px`,
            position: 'absolute',
            top: 0,
            left: 0,
            cursor: getCanvasCursor(),
          }}
          onClick={handleClick}
          onDoubleClick={handleDoubleClick}
          onMouseMove={handleMouseMove}
          onMouseDown={handleMouseDown}
          onMouseUp={handleMouseUp}
          onMouseLeave={handleMouseLeave}
        >
        {/* Floorplan background - use SVG image for perfect alignment */}
        <svg
          className="absolute inset-0 w-full h-full pointer-events-none"
          viewBox={`0 0 ${canvasWidth} ${canvasHeight}`}
          preserveAspectRatio="none"
        >
          {floorplanUrl && (
            <image
              href={floorplanUrl}
              x="0"
              y="0"
              width={canvasWidth}
              height={canvasHeight}
              opacity="0.5"
              preserveAspectRatio="none"
            />
          )}
        </svg>

        {/* SVG overlay for drawing */}
        <svg
          className="absolute inset-0 w-full h-full"
          viewBox={`0 0 ${canvasWidth} ${canvasHeight}`}
          preserveAspectRatio="none"
        >
          {/* Existing spaces */}
          {existingSpaces.map((space) => {
            // Show preview position if being dragged
            if (
              draggingSpace?.id === space.id &&
              mousePosition
            ) {
              const deltaX = mousePosition.x - draggingSpace.startPos.x;
              const deltaY = mousePosition.y - draggingSpace.startPos.y;

              const previewGeometry: SpaceGeometry = {
                type: draggingSpace.geometry.type,
                coordinates: draggingSpace.geometry.coordinates.map(
                  (coord) => ({
                    x: coord.x + deltaX,
                    y: coord.y + deltaY,
                  }),
                ),
              };

              const previewSpace = { ...space, geometry: previewGeometry };
              return (
                <SpaceShapeSvg
                  key={space.id}
                  space={previewSpace}
                  isDragging={true}
                  drawingMode={drawingMode}
                  selectedSpaceId={selectedSpaceId}
                  resizingSpace={resizingSpace}
                  mousePosition={mousePosition}
                  spaceColors={spaceColors}
                />
              );
            }

            return (
              <SpaceShapeSvg
                key={space.id}
                space={space}
                drawingMode={drawingMode}
                selectedSpaceId={selectedSpaceId}
                resizingSpace={resizingSpace}
                mousePosition={mousePosition}
                spaceColors={spaceColors}
              />
            );
          })}

          {/* Drawing preview */}
          <DrawingPreviewSvg
            drawingMode={drawingMode}
            drawingPoints={drawingPoints}
            mousePosition={mousePosition}
          />
        </svg>
        </div>
      </div>

      <CanvasInstructions
        isPassiveMode={isPassiveMode}
        drawingMode={drawingMode}
        drawingPoints={drawingPoints}
        selectedSpaceId={selectedSpaceId}
      />
    </div>
  );
}
