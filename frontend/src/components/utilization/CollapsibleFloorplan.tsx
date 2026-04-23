import { SpaceDrawingCanvas } from "@foundation/src/components/requests/SpaceDrawingCanvas";
import { Button } from "@foundation/src/components/ui/button";
import { useFloorplanViewData } from "@foundation/src/hooks/useFloorplan";
import { useSpaces } from "@foundation/src/hooks/useSpaces";
import { useAppStore } from "@foundation/src/store/app-store";
import type { Request } from "@foundation/src/types/requests";
import { ChevronDown, ChevronUp, GripHorizontal, MapPin } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Link } from "react-router-dom";

interface CollapsibleFloorplanProps {
  isCollapsed: boolean;
  onToggle: () => void;
  timeCursorTs: Date;
  requests: Request[];
  /** Set of request IDs that currently have at least one conflict — used for floorplan highlighting. */
  conflicts: ReadonlySet<string>;
  height: number;
  onHeightChange: (height: number) => void;
}

const MIN_HEIGHT = 150;
const MAX_HEIGHT = 600;

export function CollapsibleFloorplan({
  isCollapsed,
  onToggle,
  timeCursorTs,
  requests,
  conflicts,
  height,
  onHeightChange,
}: CollapsibleFloorplanProps) {
  const selectedSiteId = useAppStore((state) => state.selectedSiteId);

  const {
    data: floorplanData,
    isLoading: isLoadingFloorplan,
    error: floorplanError,
  } = useFloorplanViewData(selectedSiteId, !isCollapsed);

  const {
    data: spaces = [],
    isLoading: isLoadingSpaces,
    error: spacesError,
  } = useSpaces(selectedSiteId);

  const [isDragging, setIsDragging] = useState(false);
  const dragStartY = useRef(0);
  const dragStartHeight = useRef(0);

  // Handle resize drag via pointer events (supports mouse, touch, pen)
  const handlePointerDown = (e: React.PointerEvent) => {
    if (isCollapsed) return;
    setIsDragging(true);
    dragStartY.current = e.clientY;
    dragStartHeight.current = height;
    (e.target as HTMLElement).setPointerCapture(e.pointerId);
    e.preventDefault();
  };

  useEffect(() => {
    if (!isDragging) return;

    const handlePointerMove = (e: PointerEvent) => {
      const deltaY = e.clientY - dragStartY.current;
      const newHeight = Math.max(MIN_HEIGHT, Math.min(MAX_HEIGHT, dragStartHeight.current + deltaY));
      onHeightChange(newHeight);
    };

    const handlePointerUp = () => {
      setIsDragging(false);
    };

    document.addEventListener("pointermove", handlePointerMove);
    document.addEventListener("pointerup", handlePointerUp);

    return () => {
      document.removeEventListener("pointermove", handlePointerMove);
      document.removeEventListener("pointerup", handlePointerUp);
    };
  }, [isDragging, onHeightChange]);

  const handleResizeKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (isCollapsed) return;
      const step = e.shiftKey ? 50 : 10;
      if (e.key === "ArrowDown" || e.key === "ArrowRight") {
        e.preventDefault();
        onHeightChange(Math.min(MAX_HEIGHT, height + step));
      } else if (e.key === "ArrowUp" || e.key === "ArrowLeft") {
        e.preventDefault();
        onHeightChange(Math.max(MIN_HEIGHT, height - step));
      }
    },
    [isCollapsed, height, onHeightChange],
  );

  // Memoize cursor-time filtering
  const { occupiedSpaceIds, conflictingSpaceIds } = useMemo(() => {
    const active = requests.filter((req) => {
      if (!req.startTs || !req.endTs || !req.spaceId) return false;
      const start = new Date(req.startTs);
      const end = new Date(req.endTs);
      return start <= timeCursorTs && timeCursorTs < end;
    });

    return {
      occupiedSpaceIds: new Set(active.map((req) => req.spaceId)),
      conflictingSpaceIds: new Set(
        active.filter((req) => conflicts.has(req.id)).map((req) => req.spaceId),
      ),
    };
  }, [requests, timeCursorTs, conflicts]);

  const spacesWithGeometry = useMemo(
    () => spaces.filter((s) => s.geometry),
    [spaces],
  );

  const spaceColors = useMemo(
    () =>
      Object.fromEntries(
        spacesWithGeometry.map((space) => [
          space.id,
          conflictingSpaceIds.has(space.id)
            ? { fill: "rgba(249, 115, 22, 0.35)", stroke: "#f97316" }
            : occupiedSpaceIds.has(space.id)
              ? { fill: "rgba(239, 68, 68, 0.25)", stroke: "#ef4444" }
              : { fill: "rgba(34, 197, 94, 0.25)", stroke: "#22c55e" },
        ]),
      ),
    [spacesWithGeometry, occupiedSpaceIds, conflictingSpaceIds],
  );

  const fetchError = floorplanError || spacesError;

  return (
    <div className="border-b bg-card">
      {/* Header - Always visible */}
      <div className="h-10 px-4 flex items-center justify-between border-b">
        <div className="flex items-center gap-2">
          <h3 className="text-sm font-medium">Floorplan</h3>
        </div>
        <Button
          variant="ghost"
          size="sm"
          onClick={onToggle}
          aria-expanded={!isCollapsed}
          className="h-8 px-2"
        >
          {isCollapsed ? (
            <>
              <ChevronDown className="h-4 w-4 mr-1" />
              Expand
            </>
          ) : (
            <>
              <ChevronUp className="h-4 w-4 mr-1" />
              Collapse
            </>
          )}
        </Button>
      </div>

      {/* Floorplan content */}
      {!isCollapsed && (
        <>
          <div style={{ height: `${height}px` }} className="p-4 bg-muted overflow-hidden">
            {isLoadingFloorplan ? (
              <div className="h-full flex items-center justify-center text-muted-foreground text-sm">
                Loading floorplan...
              </div>
            ) : fetchError ? (
              <div className="h-full flex items-center justify-center text-destructive text-sm">
                Failed to load floorplan. Please try refreshing.
              </div>
            ) : !selectedSiteId ? (
              <div className="h-full flex items-center justify-center text-muted-foreground text-sm">
                Select a site to view floorplan
              </div>
            ) : !floorplanData ? (
              <div className="h-full flex items-center justify-center text-muted-foreground text-sm">
                <div className="text-center flex flex-col items-center gap-2">
                  <MapPin className="h-8 w-8 text-muted-foreground/60" aria-hidden />
                  <p>No floorplan uploaded for this site</p>
                  <Button asChild variant="default" size="sm">
                    <Link to="/spaces">Upload floorplan</Link>
                  </Button>
                </div>
              </div>
            ) : (
              <div className="h-full w-full relative">
                <SpaceDrawingCanvas
                  floorplanUrl={floorplanData.blobUrl}
                  floorplanDimensions={{
                    width: floorplanData.widthPx,
                    height: floorplanData.heightPx,
                  }}
                  drawingMode="none"
                  onDrawingComplete={() => {}}
                  onDrawingCancel={() => {}}
                  existingSpaces={spaces.map((space) => ({
                    id: space.id,
                    name: space.name,
                    code: space.code,
                    geometry: space.geometry,
                  }))}
                  zoom={1}
                  fitMode="contain"
                  spaceColors={spaceColors}
                  className="h-full w-full shadow-md border border-border rounded-lg"
                />

                {/* Occupied spaces legend */}
                <div className="absolute top-2 right-2 bg-background/90 px-3 py-2 rounded-md shadow-md text-xs space-y-1">
                  <div className="font-medium">Space Status</div>
                  {isLoadingSpaces && (
                    <div className="text-muted-foreground">Updating spaces...</div>
                  )}
                  <div className="flex items-center gap-2">
                    <div className="h-3 w-3 rounded border-2 border-green-500 bg-green-200/70" />
                    <span className="text-muted-foreground">
                      Available ({spacesWithGeometry.length - occupiedSpaceIds.size})
                    </span>
                  </div>
                  <div className="flex items-center gap-2">
                    <div className="h-3 w-3 rounded border-2 border-red-500 bg-red-200/70" />
                    <span className="text-muted-foreground">
                      Occupied ({occupiedSpaceIds.size - conflictingSpaceIds.size})
                    </span>
                  </div>
                  {conflictingSpaceIds.size > 0 && (
                    <div className="flex items-center gap-2">
                      <div className="h-3 w-3 rounded border-2 border-orange-500 bg-orange-200/70" />
                      <span className="text-muted-foreground">
                        Conflict ({conflictingSpaceIds.size})
                      </span>
                    </div>
                  )}
                </div>
              </div>
            )}
          </div>

          {/* Resize handle */}
          <div
            role="separator"
            aria-orientation="horizontal"
            aria-label="Resize floorplan panel"
            aria-valuenow={height}
            aria-valuemin={MIN_HEIGHT}
            aria-valuemax={MAX_HEIGHT}
            tabIndex={0}
            className="h-1 bg-border hover:bg-primary cursor-ns-resize group flex items-center justify-center relative touch-none"
            onPointerDown={handlePointerDown}
            onKeyDown={handleResizeKeyDown}
          >
            <GripHorizontal className="h-4 w-4 text-muted-foreground group-hover:text-primary absolute" />
          </div>
        </>
      )}
    </div>
  );
}
