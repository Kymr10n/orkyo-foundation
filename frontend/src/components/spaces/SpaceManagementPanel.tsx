import { CreateSpaceDialog } from "@foundation/src/components/requests/CreateSpaceDialog";
import { FloorplanUploadDialog } from "@foundation/src/components/requests/FloorplanUploadDialog";
import { SpaceDrawingCanvas } from "@foundation/src/components/requests/SpaceDrawingCanvas";
import { Button } from "@foundation/src/components/ui/button";
import { Separator } from "@foundation/src/components/ui/separator";
import {
  deleteFloorplan,
  getFloorplanImageUrl,
  type FloorplanMetadata,
  getFloorplanMetadata,
} from "@foundation/src/lib/api/floorplan-api";
import { qk } from "@foundation/src/lib/api/query-keys";
import { cn } from "@foundation/src/lib/utils";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import { useResourceTypes } from "@foundation/src/hooks/useResourceTypes";
import { RESOURCE_TYPE_KEY } from "@foundation/src/constants/resource-type-key";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint";
import type {
  CreateResourceRequest,
  ResourceInfo,
} from "@foundation/src/lib/api/resources-api";
import type { DrawingMode, ResourceGeometry } from "@foundation/src/types/geometry";
import {
  Check,
  Circle,
  Copy,
  MapPin,
  Pencil,
  Pentagon,
  Square,
  Trash2,
  Upload,
  ZoomIn,
  ZoomOut,
} from "lucide-react";
import { useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import { toast } from "sonner";
import { ConfirmDialog } from "@foundation/src/components/ui/ConfirmDialog";
import { EditSpaceDialog } from "./EditSpaceDialog";
import {
  usePlaceableResources,
  useCreatePlaceableResource,
  useUpdatePlaceableResource,
  useMovePlaceableResource,
  useDeletePlaceableResource,
} from "@foundation/src/hooks/usePlaceableResources";
import { duplicateResourceRequest } from "@foundation/src/lib/utils/duplicate-resource";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@foundation/src/components/ui/dropdown-menu";
import { useEditQueryParam } from "@foundation/src/hooks/useEditQueryParam";
import { logger } from "@foundation/src/lib/core/logger";

interface SpaceManagementPanelProps {
  siteId: string;
  className?: string;
}

export function SpaceManagementPanel({
  siteId,
  className,
}: SpaceManagementPanelProps) {
  // React Query hooks
  const { data: spaces = [], isLoading: isLoadingSpaces } = usePlaceableResources(siteId);
  const createSpaceMutation = useCreatePlaceableResource(siteId);
  const _updateSpaceMutation = useUpdatePlaceableResource(siteId);
  const moveSpaceMutation = useMovePlaceableResource(siteId);
  const resizeSpaceMutation = useMovePlaceableResource(siteId);
  const deleteSpaceMutation = useDeletePlaceableResource(siteId);

  const canEdit = useCanEdit();

  // What the next drawn shape becomes. Chosen before drawing rather than after, so the shape
  // means something the moment it exists. The resolution ladder is the one the create dialog used
  // to own — prefer the built-in space, else whatever the tenant defined first.
  const { data: resourceTypes = [] } = useResourceTypes(true);
  const placeableTypes = resourceTypes.filter((t) => t.hasGeometry);
  const [typeKey, setTypeKey] = useState<string | null>(null);
  const activeType =
    placeableTypes.find((t) => t.key === typeKey)
    ?? placeableTypes.find((t) => t.key === RESOURCE_TYPE_KEY.SPACE)
    ?? placeableTypes[0];
  // Phone is a read-only floorplan: editing tools (delete + drawing modes) are
  // hidden; pan/zoom navigation stays. Tablet keeps the full toolset.
  const { isPhone } = useBreakpoint();
  const [uploadDialogOpen, setUploadDialogOpen] = useState(false);
  const queryClient = useQueryClient();
  const [floorplanMetadata, setFloorplanMetadata] =
    useState<FloorplanMetadata | null>(null);
  const [floorplanBlobUrl, setFloorplanBlobUrl] = useState<string | null>(null);
  const [zoom, setZoom] = useState(1);
  const [drawingMode, setDrawingMode] = useState<DrawingMode>("none");
  const [drawnGeometry, setDrawnGeometry] = useState<ResourceGeometry | null>(
    null,
  );
  const [createDialogOpen, setCreateDialogOpen] = useState(false);
  const [deleteFloorplanOpen, setDeleteFloorplanOpen] = useState(false);
  const [isDeletingFloorplan, setIsDeletingFloorplan] = useState(false);
  const [editingSpace, setEditingSpace] = useState<ResourceInfo | null>(null);
  const [selectedResourceId, setSelectedResourceId] = useState<string | null>(null);
  // Master edit switch — view mode (pan/zoom only) by default; protects against
  // accidental, un-undoable move/resize. Double-click-to-inspect ignores this.
  const [editEnabled, setEditEnabled] = useState(false);
  // Right-click target and where to anchor its menu, in viewport coordinates.
  const [contextMenu, setContextMenu] = useState<{ id: string; x: number; y: number } | null>(null);
  const [deletingSpace, setDeletingSpace] = useState<ResourceInfo | null>(null);
  // No placeable type means a drawn shape could only ever be rejected, so the tools stay off
  // rather than letting someone do the work of drawing first.
  const canDraw = canEdit && editEnabled && !!activeType;

  // Handle ?edit=<id> query param from global search. The shared hook reads and clears the
  // param itself and guards StrictMode's double-invoked mount, which this hand-rolled copy did not.
  useEditQueryParam(spaces, setEditingSpace, { ready: !isLoadingSpaces });


  // Load floorplan metadata on mount
  useEffect(() => {
    if (siteId) {
      getFloorplanMetadata(siteId)
        .then(setFloorplanMetadata)
        .catch((err: unknown) => logger.error(err));
    }
  }, [siteId]);

  // Fetch floorplan image with auth headers and create a data URL. Keyed on the metadata
  // object, not merely on whether one exists: replacing a floorplan swaps the metadata while
  // it stays truthy, and that must refetch rather than leave the previous image on the canvas.
  // No clearing here — `floorplanUrl` below already reads through the current metadata, so a
  // stale blob url is never rendered.
  useEffect(() => {
    if (!siteId || !floorplanMetadata) return;
    let cancelled = false;
    getFloorplanImageUrl(siteId)
      .then((url) => {
        if (!cancelled) {
          setFloorplanBlobUrl(url);
        }
      })
      .catch((err: unknown) => logger.error("Failed to load floorplan image:", err));
    return () => {
      cancelled = true;
    };
  }, [siteId, floorplanMetadata]);

  const handleUploadComplete = (metadata: FloorplanMetadata) => {
    logger.debug("handleUploadComplete called with metadata:", metadata);
    setFloorplanMetadata(metadata);
  };

  const handleConfirmDeleteFloorplan = async () => {
    setIsDeletingFloorplan(true);
    try {
      await deleteFloorplan(siteId);
      await queryClient.invalidateQueries({ queryKey: qk.floorplan.viewData(siteId) });
      setFloorplanMetadata(null);
      setFloorplanBlobUrl(null);
      setDeleteFloorplanOpen(false);
    } catch (error) {
      logger.error("Failed to delete floorplan:", error);
      toast.error("Failed to delete floorplan", {
        description: error instanceof Error ? error.message : undefined,
      });
    } finally {
      setIsDeletingFloorplan(false);
    }
  };

  const handleZoomIn = () => {
    setZoom((prev) => Math.min(prev + 0.25, 3));
  };

  const handleZoomOut = () => {
    setZoom((prev) => Math.max(prev - 0.25, 0.5));
  };

  const handleZoomReset = () => {
    setZoom(1);
  };

  const handleDrawingComplete = (geometry: ResourceGeometry) => {
    setDrawnGeometry(geometry);
    setCreateDialogOpen(true);
    // The tool stays armed on purpose, so laying out a row of identical stations is
    // draw-name-save repeated rather than re-arming between each. Escape disarms, as does
    // clicking the active tool again; leaving edit mode forces it off.
  };

  const handleCancelDrawing = () => {
    setDrawingMode("none");
  };

  const handleCreateSpace = async (request: CreateResourceRequest) => {
    try {
      await createSpaceMutation.mutateAsync(request);
      setCreateDialogOpen(false);
      setDrawnGeometry(null);
    } catch (error) {
      logger.error("Failed to create space:", error);
      throw error;
    }
  };

  const handleUpdateSpace = () => {
    setEditingSpace(null);
  };

  const handleSetDrawingMode = (mode: DrawingMode) => {
    // Clicking the armed tool disarms it — with the tool staying armed after a save, this is the
    // affordance that stops it.
    setDrawingMode((current) => (current === mode ? "none" : mode));
  };

  const handleToggleEdit = () =>
    setEditEnabled((on) => {
      const next = !on;
      if (!next) {
        setDrawingMode("none");
        setSelectedResourceId(null);
      }
      return next;
    });

  const handleEditSpaceById = (resourceId: string) => {
    const space = spaces.find((s) => s.id === resourceId);
    if (space) setEditingSpace(space);
  };

  const handleSpaceContextMenu = (resourceId: string, position: { x: number; y: number }) => {
    setContextMenu({ id: resourceId, x: position.x, y: position.y });
  };

  const contextMenuSpace = spaces.find((s) => s.id === contextMenu?.id) ?? null;

  const handleDuplicateSpace = async () => {
    if (!contextMenuSpace) return;
    setContextMenu(null);
    try {
      await createSpaceMutation.mutateAsync(duplicateResourceRequest(contextMenuSpace, siteId));
    } catch (error) {
      // Feedback owned by the create mutation's meta.errorMessage (central MutationCache).
      logger.error("Failed to duplicate resource:", error);
    }
  };

  const handleConfirmDelete = async () => {
    if (!deletingSpace) return;
    try {
      await deleteSpaceMutation.mutateAsync(deletingSpace.id);
      setDeletingSpace(null);
    } catch (error) {
      // Feedback owned by the delete hook's own onError toast (optimistic-rollback hook).
      logger.error("Failed to delete resource:", error);
    }
  };

  const handleMoveSpace = async (
    resourceId: string,
    newGeometry: ResourceGeometry,
  ) => {
    try {
      await moveSpaceMutation.mutateAsync({ resourceId, geometry: newGeometry });
    } catch (error) {
      // Feedback owned by the move mutation's meta.errorMessage (central MutationCache).
      logger.error("Failed to move resource:", error);
    }
  };

  const handleResizeSpace = async (
    resourceId: string,
    newGeometry: ResourceGeometry,
  ) => {
    try {
      await resizeSpaceMutation.mutateAsync({ resourceId, geometry: newGeometry });
    } catch (error) {
      // Feedback owned by the move mutation's meta.errorMessage (central MutationCache).
      logger.error("Failed to resize resource:", error);
    }
  };

  const floorplanUrl = floorplanMetadata ? floorplanBlobUrl : null;

  const _selectedSpace = spaces.find((s) => s.id === selectedResourceId);

  return (
    <div className={cn("flex h-full", className)}>
      {/* Floorplan Panel */}
      <div className="flex-1 flex flex-col bg-card rounded-lg border">
        {/* Header with controls */}
        <div className="flex items-center justify-between p-4 border-b">
          <div className="flex items-center gap-2">
            <MapPin className="h-5 w-5 text-muted-foreground" />
            <h2 className="font-semibold">Floorplan</h2>
            {editEnabled && (
              <span className="text-xs font-medium text-primary">Editing</span>
            )}
          </div>
          <div className="flex items-center gap-2">
            {floorplanMetadata ? (
              <>
                {!isPhone && (
                <>
                <Button
                  variant={editEnabled ? "default" : "outline"}
                  size="sm"
                  onClick={handleToggleEdit}
                  disabled={!canEdit}
                  aria-pressed={editEnabled}
                >
                  {editEnabled ? (
                    <Check className="h-4 w-4 mr-2" />
                  ) : (
                    <Pencil className="h-4 w-4 mr-2" />
                  )}
                  {editEnabled ? "Done" : "Edit"}
                </Button>
                <Separator orientation="vertical" className="h-6" />
                <Button
                  variant="ghost"
                  size="icon"
                  onClick={() => setDeleteFloorplanOpen(true)}
                  disabled={!canEdit || !editEnabled}
                  title="Delete floorplan"
                  aria-label="Delete floorplan"
                >
                  <Trash2 className="h-4 w-4 text-destructive" />
                </Button>
                {/* One answer is not a question — the picker only appears when the tenant has
                    defined more than one thing that can occupy area. */}
                {placeableTypes.length > 1 && (
                  <Select
                    value={activeType?.key ?? ""}
                    onValueChange={setTypeKey}
                    disabled={!canEdit || !editEnabled}
                  >
                    <SelectTrigger className="h-9 w-40" aria-label="Station type">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {placeableTypes.map((type) => (
                        <SelectItem key={type.id} value={type.key}>
                          {type.displayName}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
                <div className="flex items-center gap-1">
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={() => handleSetDrawingMode("rectangle")}
                    disabled={!canDraw}
                    title="Draw Rectangle (R)"
                    aria-label="Draw rectangle"
                    aria-pressed={drawingMode === "rectangle"}
                  >
                    <Square
                      className={cn(
                        "h-4 w-4",
                        drawingMode === "rectangle" && "text-primary",
                      )}
                    />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={() => handleSetDrawingMode("polygon")}
                    disabled={!canDraw}
                    title="Draw Polygon (P)"
                    aria-label="Draw polygon"
                    aria-pressed={drawingMode === "polygon"}
                  >
                    <Pentagon
                      className={cn(
                        "h-4 w-4",
                        drawingMode === "polygon" && "text-primary",
                      )}
                    />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={() => handleSetDrawingMode("circle")}
                    disabled={!canDraw}
                    title="Draw Circle (C)"
                    aria-label="Draw circle"
                    aria-pressed={drawingMode === "circle"}
                  >
                    <Circle
                      className={cn(
                        "h-4 w-4",
                        drawingMode === "circle" && "text-primary",
                      )}
                    />
                  </Button>
                </div>
                <Separator orientation="vertical" className="h-6" />
                </>
                )}
                <div className="flex items-center gap-1">
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={handleZoomOut}
                    disabled={zoom <= 0.5}
                    aria-label="Zoom out"
                  >
                    <ZoomOut className="h-4 w-4" />
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={handleZoomReset}
                    className="min-w-[3rem] h-8 text-xs"
                    title="Reset zoom to 100%"
                  >
                    {Math.round(zoom * 100)}%
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={handleZoomIn}
                    disabled={zoom >= 3}
                    aria-label="Zoom in"
                  >
                    <ZoomIn className="h-4 w-4" />
                  </Button>
                </div>
              </>
            ) : (
              <Button onClick={() => setUploadDialogOpen(true)} size="sm" disabled={!canEdit}>
                <Upload className="h-4 w-4 mr-2" />
                Upload Floorplan
              </Button>
            )}
          </div>
        </div>

        {/* Canvas */}
        <div
          className={cn(
            "flex-1 overflow-auto p-4",
            editEnabled && "ring-2 ring-inset ring-primary rounded-md",
          )}
        >
          {floorplanMetadata ? (
            <SpaceDrawingCanvas
              floorplanUrl={floorplanUrl || undefined}
              floorplanDimensions={{
                width: floorplanMetadata.widthPx,
                height: floorplanMetadata.heightPx,
              }}
              zoom={zoom}
              drawingMode={drawingMode}
              onDrawingComplete={handleDrawingComplete}
              onDrawingCancel={handleCancelDrawing}
              existingSpaces={spaces}
              editEnabled={canEdit && !isPhone && editEnabled}
              selectedResourceId={selectedResourceId || undefined}
              onSpaceClick={setSelectedResourceId}
              onSpaceDoubleClick={canEdit && !isPhone ? handleEditSpaceById : undefined}
          onSpaceContextMenu={canEdit && !isPhone ? handleSpaceContextMenu : undefined}
              onSpaceMove={canEdit && !isPhone ? handleMoveSpace : undefined}
              onSpaceResize={canEdit && !isPhone ? handleResizeSpace : undefined}
            />
          ) : (
            <div className="flex items-center justify-center h-full text-muted-foreground">
              <div className="text-center">
                <MapPin className="h-12 w-12 mx-auto mb-4 opacity-50" />
                <p className="text-sm mb-2">No floorplan uploaded</p>
                <Button onClick={() => setUploadDialogOpen(true)} size="sm">
                  <Upload className="h-4 w-4 mr-2" />
                  Upload Floorplan
                </Button>
              </div>
            </div>
          )}
        </div>

        {/* Dialogs */}
        <FloorplanUploadDialog
          siteId={siteId}
          open={uploadDialogOpen}
          onOpenChange={setUploadDialogOpen}
          onUploadComplete={handleUploadComplete}
        />

        {/* Anchored at the pointer via a zero-size fixed trigger. The canvas is an SVG overlay
            with delegated hit-testing, so there is no per-shape element to hang a menu off — and a
            context-menu primitive would mean a new peer dependency in three repos for two items. */}
        {contextMenu && contextMenuSpace && (
          <DropdownMenu open onOpenChange={(open) => !open && setContextMenu(null)}>
            <DropdownMenuTrigger asChild>
              <span
                aria-hidden
                style={{
                  position: "fixed",
                  left: contextMenu.x,
                  top: contextMenu.y,
                  width: 0,
                  height: 0,
                }}
              />
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start">
              <DropdownMenuItem onSelect={handleDuplicateSpace}>
                <Copy className="mr-2 h-4 w-4" />
                Duplicate
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem
                className="text-destructive focus:text-destructive"
                onSelect={() => {
                  setDeletingSpace(contextMenuSpace);
                  setContextMenu(null);
                }}
              >
                <Trash2 className="mr-2 h-4 w-4" />
                Delete
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
        )}

        <ConfirmDialog
          open={!!deletingSpace}
          onOpenChange={(open) => !open && setDeletingSpace(null)}
          title="Delete station"
          description={`Delete "${deletingSpace?.name ?? ""}"? It stops appearing on the floorplan and in the grid, but its assignment history is kept.`}
          confirmLabel="Delete"
          destructive
          isPending={deleteSpaceMutation.isPending}
          onConfirm={handleConfirmDelete}
        />

        {/* Both conditions hold by construction — drawing requires an armed type — and gating on
            them here removes the dead space-key fallbacks the dialog used to carry. */}
        {drawnGeometry && activeType && (
          <CreateSpaceDialog
            open={createDialogOpen}
            onOpenChange={setCreateDialogOpen}
            geometry={drawnGeometry}
            resourceTypeKey={activeType.key}
            resourceTypeId={activeType.id}
            resourceTypeLabel={activeType.displayName}
            onSubmit={handleCreateSpace}
            siteId={siteId}
          />
        )}
        {editingSpace && (
          <EditSpaceDialog
            space={editingSpace}
            siteId={siteId}
            open={!!editingSpace}
            onOpenChange={(open) => !open && setEditingSpace(null)}
            onSuccess={handleUpdateSpace}
          />
        )}

        <ConfirmDialog
          open={deleteFloorplanOpen}
          onOpenChange={setDeleteFloorplanOpen}
          title="Delete floorplan"
          description="Are you sure you want to delete the floorplan image?"
          confirmLabel="Delete"
          destructive
          isPending={isDeletingFloorplan}
          onConfirm={handleConfirmDeleteFloorplan}
        />
      </div>
    </div>
  );
}
