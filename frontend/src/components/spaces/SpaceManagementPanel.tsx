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
import type {
  CreateSpaceRequest,
  DrawingMode,
  SpaceGeometry,
  Space as SpaceType,
} from "@foundation/src/types/space";
import {
  MapPin,
  Maximize2,
  MousePointer2,
  Pentagon,
  Square,
  Trash2,
  Upload,
  ZoomIn,
  ZoomOut,
} from "lucide-react";
import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { EditSpaceDialog } from "./EditSpaceDialog";
import { SpaceCapabilitiesEditor } from "./SpaceCapabilitiesEditor";
import { SpaceList } from "./SpaceList";
import { useExportHandler, useImportHandler } from "@foundation/src/hooks/useImportExport";
import { exportSpaces, importSpaces } from "@foundation/src/lib/utils/export-handlers";
import {
  useSpaces,
  useCreateSpace,
  useUpdateSpace,
  useDeleteSpace,
  useMoveSpace,
} from "@foundation/src/hooks/useSpaces";
import { logger } from "@foundation/src/lib/core/logger";

interface SpaceManagementPanelProps {
  siteId: string;
  editSpaceId?: string | null;
  className?: string;
}

export function SpaceManagementPanel({
  siteId,
  editSpaceId,
  className,
}: SpaceManagementPanelProps) {
  // React Query hooks
  const { data: spaces = [], isLoading: isLoadingSpaces } = useSpaces(siteId);
  const createSpaceMutation = useCreateSpace(siteId);
  const _updateSpaceMutation = useUpdateSpace(siteId);
  const deleteSpaceMutation = useDeleteSpace(siteId);
  const moveSpaceMutation = useMoveSpace(siteId);
  const resizeSpaceMutation = useMoveSpace(siteId);

  const [uploadDialogOpen, setUploadDialogOpen] = useState(false);
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
  const [capabilitiesSpace, setCapabilitiesSpace] = useState<SpaceType | null>(null);
  const [selectedSpaceId, setSelectedSpaceId] = useState<string | null>(null);
  const [, setSearchParams] = useSearchParams();

  // Handle ?edit=<id> query param from global search
  useEffect(() => {
    if (editSpaceId && spaces.length > 0 && !isLoadingSpaces) {
      const spaceToEdit = spaces.find(s => s.id === editSpaceId);
      if (spaceToEdit) {
        setEditingSpace(spaceToEdit);
        // Clear the query param
        setSearchParams((prev) => {
          prev.delete('edit');
          return prev;
        }, { replace: true });
      }
    }
  }, [editSpaceId, spaces, isLoadingSpaces, setSearchParams]);

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

  const handleDeleteSpace = async (spaceId: string) => {
    if (deleteSpaceMutation.isPending) return;
    try {
      await deleteSpaceMutation.mutateAsync(spaceId);
      if (selectedSpaceId === spaceId) {
        setSelectedSpaceId(null);
      }
    } catch (error) {
      logger.error("Failed to delete space:", error);
      alert("Failed to delete space");
    }
  };

  const handleMoveSpace = async (
    spaceId: string,
    newGeometry: SpaceGeometry,
  ) => {
    try {
      const space = spaces.find((s) => s.id === spaceId);
      if (!space) return;

      await moveSpaceMutation.mutateAsync({ spaceId, space, newGeometry });
    } catch (error) {
      logger.error("Failed to move space:", error);
      alert("Failed to move space");
    }
  };

  const handleResizeSpace = async (
    spaceId: string,
    newGeometry: SpaceGeometry,
  ) => {
    try {
      const space = spaces.find((s) => s.id === spaceId);
      if (!space) return;

      await resizeSpaceMutation.mutateAsync({ spaceId, space, newGeometry });
    } catch (error) {
      logger.error("Failed to resize space:", error);
      alert("Failed to resize space");
    }
  };

  const floorplanUrl = floorplanMetadata ? floorplanBlobUrl : null;

  const _selectedSpace = spaces.find((s) => s.id === selectedSpaceId);

  return (
    <div className={cn("flex h-full gap-4", className)}>
      {/* Space List Sidebar */}
      <div className="w-80 flex flex-col bg-card rounded-lg border">
        <div className="p-4 border-b">
          <h3 className="font-semibold">Spaces ({spaces.length})</h3>
        </div>
        <SpaceList
          spaces={spaces}
          selectedSpaceId={selectedSpaceId}
          onSpaceSelect={setSelectedSpaceId}
          onSpaceEdit={setEditingSpace}
          onSpaceDelete={handleDeleteSpace}
          onCapabilitiesEdit={setCapabilitiesSpace}
          isLoading={isLoadingSpaces}
        />
      </div>

      {/* Main Content Area */}
      <div className="flex-1 flex flex-col gap-4">
        {/* Floorplan Panel */}
        <div className="flex-1 flex flex-col bg-card rounded-lg border">
          {/* Header with controls */}
          <div className="flex items-center justify-between p-4 border-b">
            <div className="flex items-center gap-2">
              <MapPin className="h-5 w-5 text-muted-foreground" />
              <h2 className="font-semibold">Floorplan & Spaces</h2>
            </div>
            <div className="flex items-center gap-2">
              {floorplanMetadata ? (
                <>
                  <Button
                    variant="ghost"
                    size="icon"
                    onClick={handleDeleteFloorplan}
                    title="Delete floorplan"
                  >
                    <Trash2 className="h-4 w-4 text-destructive" />
                  </Button>
                  <Separator orientation="vertical" className="h-6" />
                  <div className="flex items-center gap-1">
                    <Button
                      variant="ghost"
                      size="icon"
                      onClick={() => handleSetDrawingMode("select")}
                      title="Select (S)"
                    >
                      <MousePointer2
                        className={cn(
                          "h-4 w-4",
                          drawingMode === "select" && "text-primary",
                        )}
                      />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      onClick={() => handleSetDrawingMode("resize")}
                      title="Resize (Z)"
                    >
                      <Maximize2
                        className={cn(
                          "h-4 w-4",
                          drawingMode === "resize" && "text-primary",
                        )}
                      />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      onClick={() => handleSetDrawingMode("rectangle")}
                      title="Draw Rectangle (R)"
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
                      title="Draw Polygon (P)"
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
                  <div className="flex items-center gap-1">
                    <Button
                      variant="ghost"
                      size="icon"
                      onClick={handleZoomOut}
                      disabled={zoom <= 0.5}
                    >
                      <ZoomOut className="h-4 w-4" />
                    </Button>
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={handleZoomReset}
                      className="min-w-[3rem] h-8 text-xs"
                    >
                      {Math.round(zoom * 100)}%
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      onClick={handleZoomIn}
                      disabled={zoom >= 3}
                    >
                      <ZoomIn className="h-4 w-4" />
                    </Button>
                  </div>
                </>
              ) : (
                <Button onClick={() => setUploadDialogOpen(true)} size="sm">
                  <Upload className="h-4 w-4 mr-2" />
                  Upload Floorplan
                </Button>
              )}
            </div>
          </div>

          {/* Canvas */}
          <div className="flex-1 overflow-auto p-4">
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
                selectedSpaceId={selectedSpaceId || undefined}
                onSpaceClick={setSelectedSpaceId}
                onSpaceMove={handleMoveSpace}
                onSpaceResize={handleResizeSpace}
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
          {capabilitiesSpace && (
            <SpaceCapabilitiesEditor
              open={!!capabilitiesSpace}
              onOpenChange={(open) => !open && setCapabilitiesSpace(null)}
              siteId={siteId}
              spaceId={capabilitiesSpace.id}
              spaceName={capabilitiesSpace.name}
            />
          )}
        </div>
      </div>
    </div>
  );
}
