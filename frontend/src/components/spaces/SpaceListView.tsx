import { useState } from 'react';
import { useAppStore } from '@foundation/src/store/app-store';
import { useSpaces, useDeleteSpace } from '@foundation/src/hooks/useSpaces';
import type { Space } from '@foundation/src/types/space';
import { EditSpaceDialog } from './EditSpaceDialog';
import { SpaceCapabilitiesEditor } from './SpaceCapabilitiesEditor';
import { logger } from '@foundation/src/lib/core/logger';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { Button } from '@foundation/src/components/ui/button';
import { Square, Pentagon, Edit, Trash2, Settings } from 'lucide-react';

/**
 * Spaces tab content under /spaces/list — a focused, list-only view built on
 * OrkyoDataTable. Companion to FloorplanView which keeps the canvas-centric UX.
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

  const columns: ColumnDef<Space>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      cell: ({ row }) => {
        const space = row.original;
        const GeometryIcon = space.geometry?.type === 'rectangle' ? Square : Pentagon;
        return (
          <div className="flex items-center gap-2">
            <GeometryIcon className="h-4 w-4 text-muted-foreground shrink-0" />
            <span className="font-semibold">{space.name}</span>
            {space.code && (
              <span className="text-xs text-muted-foreground font-mono">{space.code}</span>
            )}
          </div>
        );
      },
    },
    {
      id: 'description',
      header: 'Description',
      cell: ({ row }) => (
        <span className="text-sm text-muted-foreground">
          {row.original.description ?? '—'}
        </span>
      ),
    },
    {
      id: 'actions',
      header: () => null,
      size: 120,
      cell: ({ row }) => {
        const space = row.original;
        return (
          <div className="flex justify-end gap-1">
            <Button
              variant="ghost"
              size="icon"
              className="h-7 w-7"
              onClick={(e) => { e.stopPropagation(); setCapabilitiesSpace(space); }}
              title="Edit Capabilities"
              aria-label={`Edit capabilities for ${space.name}`}
            >
              <Settings className="h-3.5 w-3.5" />
            </Button>
            <Button
              variant="ghost"
              size="icon"
              className="h-7 w-7"
              onClick={(e) => { e.stopPropagation(); setEditingSpace(space); }}
              title="Edit Space"
              aria-label={`Edit space ${space.name}`}
            >
              <Edit className="h-3.5 w-3.5" />
            </Button>
            <Button
              variant="ghost"
              size="icon"
              className="h-7 w-7 text-destructive hover:text-destructive"
              onClick={(e) => { e.stopPropagation(); handleDelete(space.id); }}
              title="Delete Space"
              aria-label={`Delete space ${space.name}`}
            >
              <Trash2 className="h-3.5 w-3.5 text-destructive" />
            </Button>
          </div>
        );
      },
    },
  ];

  return (
    <div className="h-full flex flex-col bg-card rounded-lg border">
      <div className="p-4 border-b">
        <h3 className="font-semibold">Spaces ({spaces.length})</h3>
      </div>
      <div className="flex-1 min-h-0 p-4 overflow-auto">
        <OrkyoDataTable
          columns={columns}
          data={spaces}
          isLoading={isLoading}
          filterColumn="name"
          filterPlaceholder="Search spaces..."
          emptyMessage="No spaces created yet. Draw a rectangle or polygon on the floorplan."
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
