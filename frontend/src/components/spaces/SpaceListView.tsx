import { useState } from 'react';
import { useAppStore } from '@foundation/src/store/app-store';
import { useSpaces, useDeleteSpace } from '@foundation/src/hooks/useSpaces';
import type { Space } from '@foundation/src/types/space';
import { EditSpaceDialog } from './EditSpaceDialog';
import { SpaceCapabilitiesEditor } from './SpaceCapabilitiesEditor';
import { logger } from '@foundation/src/lib/core/logger';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { RowActions } from '@foundation/src/components/ui/RowActions';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { Edit, Trash2, Settings } from 'lucide-react';

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
  const [deletingSpace, setDeletingSpace] = useState<Space | null>(null);
  const canEdit = useCanEdit();

  if (!selectedSiteId) {
    return (
      <div className="rounded-2xl border bg-card p-6">
        <p className="text-muted-foreground">Please select a site to manage spaces.</p>
      </div>
    );
  }

  const handleConfirmDelete = async () => {
    if (!deletingSpace) return;
    try {
      await deleteSpaceMutation.mutateAsync(deletingSpace.id);
      setDeletingSpace(null);
    } catch (err) {
      // Feedback owned by useDeleteSpace's onError toast (optimistic-rollback hook).
      logger.error('Failed to delete space:', err);
    }
  };

  // Shared cell fragments — used by both the desktop table columns and the phone
  // card so the two presentations never drift. Each action stops propagation so
  // it doesn't trigger the row/card onClick.
  const renderName = (space: Space) => (
    <div className="flex items-center gap-2">
      <span className="font-medium">{space.name}</span>
      {space.code && (
        <span className="text-xs text-muted-foreground font-mono">{space.code}</span>
      )}
    </div>
  );

  const renderActions = (space: Space) => (
    <RowActions
      triggerLabel={`Actions for ${space.name}`}
      actions={[
        {
          label: 'Edit Capabilities',
          icon: Settings,
          onSelect: () => setCapabilitiesSpace(space),
          disabled: !canEdit,
        },
        {
          label: 'Edit Space',
          icon: Edit,
          onSelect: () => setEditingSpace(space),
          disabled: !canEdit,
        },
        {
          label: 'Delete Space',
          icon: Trash2,
          onSelect: () => setDeletingSpace(space),
          disabled: !canEdit,
          destructive: true,
        },
      ]}
    />
  );

  const columns: ColumnDef<Space>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      cell: ({ row }) => renderName(row.original),
    },
    {
      id: 'description',
      header: 'Description',
      cell: ({ row }) => (
        <span className="text-muted-foreground">
          {row.original.description ?? '—'}
        </span>
      ),
    },
    {
      id: 'actions',
      header: () => null,
      size: 60,
      cell: ({ row }) => renderActions(row.original),
    },
  ];

  // Phone presentation: name + code on top, description below, actions trailing.
  const renderCard = (space: Space) => (
    <div className="flex items-start justify-between gap-2">
      <div className="min-w-0 space-y-1">
        {renderName(space)}
        {space.description && (
          <p className="text-sm text-muted-foreground">{space.description}</p>
        )}
      </div>
      {renderActions(space)}
    </div>
  );

  return (
    <div className="space-y-4">
      <OrkyoDataTable
        columns={columns}
        data={spaces}
        isLoading={isLoading}
        filterColumn="name"
        filterPlaceholder="Search spaces..."
        emptyMessage="No spaces created yet. Draw a rectangle or polygon on the floorplan."
        renderCard={renderCard}
        onRowClick={canEdit ? (space) => setEditingSpace(space) : undefined}
        pageSize={25}
      />

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

      <ConfirmDialog
        open={!!deletingSpace}
        onOpenChange={(open) => !open && setDeletingSpace(null)}
        title="Delete space"
        description={
          deletingSpace
            ? `Delete "${deletingSpace.name}"? This cannot be undone.`
            : ''
        }
        confirmLabel="Delete"
        destructive
        isPending={deleteSpaceMutation.isPending}
        onConfirm={handleConfirmDelete}
      />
    </div>
  );
}
