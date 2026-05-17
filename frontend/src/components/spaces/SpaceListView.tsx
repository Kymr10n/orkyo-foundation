import { useState } from 'react';
import { useAppStore } from '@foundation/src/store/app-store';
import { useSpaces, useDeleteSpace } from '@foundation/src/hooks/useSpaces';
import type { Space } from '@foundation/src/types/space';
import { SpaceList } from './SpaceList';
import { EditSpaceDialog } from './EditSpaceDialog';
import { SpaceCapabilitiesEditor } from './SpaceCapabilitiesEditor';
import { logger } from '@foundation/src/lib/core/logger';

/**
 * Spaces tab content under /spaces/list — a focused, list-only view that
 * reuses the existing SpaceList row component. Companion to FloorplanView
 * (rendered as the /spaces/floorplan tab) which keeps the canvas-centric UX.
 */
export function SpaceListView() {
  const selectedSiteId = useAppStore((state) => state.selectedSiteId);
  const { data: spaces = [], isLoading } = useSpaces(selectedSiteId);
  const deleteSpaceMutation = useDeleteSpace(selectedSiteId ?? '');

  const [editingSpace, setEditingSpace] = useState<Space | null>(null);
  const [capabilitiesSpace, setCapabilitiesSpace] = useState<Space | null>(null);

  if (!selectedSiteId) {
    return (
      <div className="rounded-2xl border bg-card p-6">
        <p className="text-muted-foreground">Please select a site to manage spaces.</p>
      </div>
    );
  }

  const handleDelete = async (resourceId: string) => {
    if (!confirm('Delete this space? This cannot be undone.')) return;
    try {
      await deleteSpaceMutation.mutateAsync(resourceId);
    } catch (err) {
      logger.error('Failed to delete space:', err);
      alert('Failed to delete space');
    }
  };

  return (
    <div className="h-full flex flex-col bg-card rounded-lg border">
      <div className="p-4 border-b">
        <h3 className="font-semibold">Spaces ({spaces.length})</h3>
      </div>
      <div className="flex-1 min-h-0">
        <SpaceList
          spaces={spaces}
          onSpaceSelect={() => undefined}
          onSpaceEdit={setEditingSpace}
          onSpaceDelete={handleDelete}
          onCapabilitiesEdit={setCapabilitiesSpace}
          isLoading={isLoading}
        />
      </div>

      {editingSpace && (
        <EditSpaceDialog
          space={editingSpace}
          siteId={selectedSiteId}
          open={!!editingSpace}
          onOpenChange={(open) => !open && setEditingSpace(null)}
          onSuccess={() => setEditingSpace(null)}
        />
      )}
      {capabilitiesSpace && (
        <SpaceCapabilitiesEditor
          open={!!capabilitiesSpace}
          onOpenChange={(open) => !open && setCapabilitiesSpace(null)}
          siteId={selectedSiteId}
          resourceId={capabilitiesSpace.id}
          spaceName={capabilitiesSpace.name}
        />
      )}
    </div>
  );
}
