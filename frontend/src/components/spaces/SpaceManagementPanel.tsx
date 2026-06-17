import { CreateSpaceDialog } from "@foundation/src/components/requests/CreateSpaceDialog";
import { FloorplanUploadDialog } from "@foundation/src/components/requests/FloorplanUploadDialog";
import { SpaceDrawingCanvas } from "@foundation/src/components/requests/SpaceDrawingCanvas";
import { Button } from "@foundation/src/components/ui/button";
import { Separator } from "@foundation/src/components/ui/separator";
import {
  deleteFloorplan,
  fetchFloorplanImageUrl,
  type FloorplanMetadata,
  getFloorplanMetadata,
} from "@foundation/src/lib/api/floorplan-api";
import { cn } from "@foundation/src/lib/utils";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint";
import type {
  CreateSpaceRequest,
  DrawingMode,
  SpaceGeometry,
  Space as SpaceType,
} from "@foundation/src/types/space";
import {
  Check,
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
import { useSearchParams } from "react-router-dom";
import { EditSpaceDialog } from "./EditSpaceDialog";
import { useExportHandler, useImportHandler } from "@foundation/src/hooks/useImportExport";
import { exportSpaces, importSpaces } from "@foundation/src/lib/utils/export-handlers";
import {
  useSpaces,
  useCreateSpace,
  useUpdateSpace,
  useMoveSpace,
} from "@foundation/src/hooks/useSpaces";
import { logger } from "@foundation/src/lib/core/logger";

interface SpaceManagementPanelProps {
  siteId: string;
  editResourceId?: string | null;
  className?: string;
}

export function SpaceManagementPanel({
  siteId,
  editResourceId,
  className,
}: SpaceManagementPanelProps) {
  // React Query hooks
  const { data: spaces = [], isLoading: isLoadingSpaces } = useSpaces(siteId);
  const createSpaceMutation = useCreateSpace(siteId);
  const _updateSpaceMutation = useUpdateSpace(siteId);
  const moveSpaceMutation = useMoveSpace(siteId);
  const resizeSpaceMutation = useMoveSpace(siteId);

  const canEdit = useCanEdit();
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
  const [drawnGeometry, setDrawnGeometry] = useState<SpaceGeometry | null>(
    null,
  );
  const [createDialogOpen, setCreateDialogOpen] = useState(false);
  const [editingSpace, setEditingSpace] = useState<SpaceType | null>(null);
  const [selectedResourceId, setSelectedResourceId] = useState<string | null>(null);
  // Master edit switch — view mode (pan/zoom only) by default; protects against
  // accidental, un-undoable move/resize. Double-click-to-inspect ignores this.
  const [editEnabled, setEditEnabled] = useState(false);
  const [, setSearchParams] = useSearchParams();

  // Handle ?edit=<id> query param from global search
  useEffect(() => {
    if (editResourceId && spaces.length > 0 && !isLoadingSpaces) {
      const spaceToEdit = spaces.find(s => s.id === editResourceId);
      if (spaceToEdit) {
        setEditingSpace(spaceToEdit);
        // Clear the query param
        setSearchParams((prev) => {
          prev.delete('edit');
          return prev;
        }, { replace: true });
      }
    }
  }, [editResourceId, spaces, isLoadingSpaces, setSearchParams]);

  // Handle export/import
  useExportHandler('spaces', async (format) => {
    await exportSpaces(spaces, format, siteId);
    logger.info(`Exported ${spaces.length} spaces as ${format.toUpperCase()}`);
  });

  useImportHandler('spaces', async (file, format) => {
    try {
      const importedSpaces = await importSpaces(file, format);
      if (!importedSpaces.length) {
        throw new Error('No valid spaces found in file');
      }
      // Create spaces via API
      for (const space of importedSpaces) {
        await createSpaceMutation.mutateAsync(space as CreateSpaceRequest);
      }
      alert(`Successfully imported ${importedSpaces.length} spaces`);
    } catch (error) {
      logger.error('Import failed:', error);
      alert(error instanceof Error ? error.message : 'Failed to import spaces');
    }
  });

  // Load floorplan metadata on mount
  useEffect(() => {
    if (siteId) {
      getFloorplanMetadata(siteId)
        .then(setFloorplanMetadata)
        .catch((err: unknown) => logger.error(err));
    }
  }, [siteId]);

  // Fetch floorplan image with auth headers and create a data URL
  useEffect(() => {
    if (!siteId || !floorplanMetadata) {
      setFloorplanBlobUrl(null);
      return;
    }
    let cancelled = false;
    fetchFloorplanImageUrl(siteId)
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

  const handleDeleteFloorplan = async () => {
    if (!confirm("Are you sure you want to delete the floorplan image?")) {
      return;
    }

    try {
      await deleteFloorplan(siteId);
      await queryClient.invalidateQueries({ queryKey: ['floorplan-view-data', siteId] });
      setFloorplanMetadata(null);
      setFloorplanBlobUrl(null);
    } catch (error) {
      logger.error("Failed to delete floorplan:", error);
      alert("Failed to delete floorplan");
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

  const handleDrawingComplete = (geometry: SpaceGeometry) => {
    setDrawnGeometry(geometry);
    setCreateDialogOpen(true);
    setDrawingMode("none");
  };

  const handleCancelDrawing = () => {
    setDrawingMode("none");
  };

  const handleCreateSpace = async (request: CreateSpaceRequest) => {
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
    setDrawingMode(mode);
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

  const handleMoveSpace = async (
    resourceId: string,
    newGeometry: SpaceGeometry,
  ) => {
    try {
      const space = spaces.find((s) => s.id === resourceId);
      if (!space) return;

      await moveSpaceMutation.mutateAsync({ resourceId, space, newGeometry });
    } catch (error) {
      logger.error("Failed to move space:", error);
      alert("Failed to move space");
    }
  };

  const handleResizeSpace = async (
    resourceId: string,
    newGeometry: SpaceGeometry,
  ) => {
    try {
      const space = spaces.find((s) => s.id === resourceId);
      if (!space) return;

      await resizeSpaceMutation.mutateAsync({ resourceId, space, newGeometry });
    } catch (error) {
      logger.error("Failed to resize space:", error);
      alert("Failed to resize space");
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
                  onClick={handleDeleteFloorplan}
                  disabled={!canEdit || !editEnabled}
                  title="Delete floorplan"
                  aria-label="Delete floorplan"
                >
                  <Trash2 className="h-4 w-4 text-destructive" />
                </Button>
                <div className="flex items-center gap-1">
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={() => handleSetDrawingMode("rectangle")}
                    disabled={!canEdit || !editEnabled}
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
                    disabled={!canEdit || !editEnabled}
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

        {drawnGeometry && (
          <CreateSpaceDialog
            open={createDialogOpen}
            onOpenChange={setCreateDialogOpen}
            geometry={drawnGeometry}
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
      </div>
    </div>
  );
}
