import { useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { CalendarOff, Pencil, Plus, Sliders, Trash2 } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { StatusBadge } from '@foundation/src/components/ui/status-badge';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { RowActions } from '@foundation/src/components/ui/RowActions';
import { ResourceEditDialog } from './ResourceEditDialog';
import { ResourceAbsenceList } from './ResourceAbsenceList';
import { ResourceCapabilitiesEditor } from './ResourceCapabilitiesEditor';
import {
  deleteResource,
  getResources,
  type ResourceInfo,
} from '@foundation/src/lib/api/resources-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';
import { useTableUrlState } from '@foundation/src/hooks/useTableUrlState';


interface ResourceListProps {
  resourceType: ResourceTypeInfo;
}

/**
 * List/create/edit for any resource type, with the same per-row reach the dedicated Spaces
 * and People pages have: criterion values, absences and deactivation. A tenant-defined type
 * is not a second-class citizen — the only thing it lacks is a bespoke page.
 */
export function ResourceList({ resourceType }: ResourceListProps) {
  const canEdit = useCanEdit();
  const [editing, setEditing] = useState<ResourceInfo | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [removing, setRemoving] = useState<ResourceInfo | null>(null);
  const [capabilitiesFor, setCapabilitiesFor] = useState<ResourceInfo | null>(null);
  const [absencesFor, setAbsencesFor] = useState<ResourceInfo | null>(null);


  const {
    data: resources,
    isLoading,
    error,
    refetch,
  } = useQuery({
    queryKey: qk.resources.byType(resourceType.key),
    queryFn: () => getResources({ resourceTypeKey: resourceType.key }),
  });

  const deleteMutation = useMutation({
    mutationFn: (id: string) => deleteResource(id),
    meta: {
      successMessage: `${resourceType.displayName} deactivated`,
      errorMessage: `Failed to deactivate ${resourceType.displayName.toLowerCase()}`,
      invalidates: [qk.resources.byType(resourceType.key), qk.resources.allFlat()],
    },
    onSuccess: () => setRemoving(null),
  });

  const label = resourceType.displayName;

  // Shared by the desktop table cell and the phone card, so both surfaces expose the same
  // reach — an action available on one but not the other is the bug this avoids.
  const renderActions = (r: ResourceInfo) => (
    <RowActions
      triggerLabel={`Actions for ${r.name}`}
      actions={[
        { label: `Edit ${label}`, icon: Pencil, onSelect: () => setEditing(r), disabled: !canEdit },
        {
          label: 'Manage Capabilities',
          icon: Sliders,
          onSelect: () => setCapabilitiesFor(r),
          disabled: !canEdit,
        },
        {
          label: 'Manage Absences',
          icon: CalendarOff,
          onSelect: () => setAbsencesFor(r),
          disabled: !canEdit,
        },
        {
          label: `Deactivate ${label}`,
          icon: Trash2,
          onSelect: () => setRemoving(r),
          disabled: !canEdit,
          destructive: true,
        },
      ]}
    />
  );

  const columns: ColumnDef<ResourceInfo>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      meta: { filter: { type: 'text' } },
      cell: ({ row }) => (
        <div className="flex items-center gap-2">
          <span className="font-medium">{row.original.name}</span>
          {!row.original.isActive && <StatusBadge status="inactive" label="Inactive" />}
        </div>
      ),
    },
    {
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      size: 100,
      cell: ({ row }) => renderActions(row.original),
    },
  ];

  const renderCard = (r: ResourceInfo) => (
    <div className="flex items-start justify-between gap-2">
      <div className="min-w-0 space-y-1">
        <div className="flex items-center gap-2">
          <span className="truncate font-medium">{r.name}</span>
          {!r.isActive && <StatusBadge status="inactive" label="Inactive" />}
        </div>
      </div>
      {renderActions(r)}
    </div>
  );

  const errorMsg =
    error instanceof Error
      ? error.message
      : error
        ? `Failed to load ${resourceType.displayName.toLowerCase()}`
        : null;

  // Header sort/filter state lives in the URL: bookmarkable, shareable, Back-safe.
  const tableUrlState = useTableUrlState('resources', columns);

  return (
    <div className="space-y-4">
      {/* No heading: ResourcesPage already titles the page with this type's plural name and
          its description. Matches the Groups tab, which is just its Add button. */}
      <div className="flex justify-end">
        {canEdit && (
          <Button onClick={() => setCreateOpen(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Add {resourceType.displayName}
          </Button>
        )}
      </div>

      <OrkyoDataTable
        {...tableUrlState}
        // Opening the editor is what a row is for; the action menu stops propagation so its
        // own items still win. Viewers get no row click — the dialog would be read-only.
        onRowClick={canEdit ? (r) => setEditing(r) : undefined}
        columns={columns}
        data={resources?.data ?? []}
        isLoading={isLoading}
        error={errorMsg}
        onRetry={() => refetch()}
        emptyMessage={`No ${resourceType.displayName.toLowerCase()} recorded yet.`}
        renderCard={renderCard}
      />

      <ResourceEditDialog
        resourceType={resourceType}
        resource={null}
        open={createOpen}
        onOpenChange={setCreateOpen}
      />
      {editing && (
        <ResourceEditDialog
          resourceType={resourceType}
          resource={editing}
          open={!!editing}
          onOpenChange={(open) => !open && setEditing(null)}
        />
      )}

      {capabilitiesFor && (
        <ResourceCapabilitiesEditor
          open={!!capabilitiesFor}
          onOpenChange={(open) => !open && setCapabilitiesFor(null)}
          resourceId={capabilitiesFor.id}
          resourceName={capabilitiesFor.name}
          resourceTypeKey={resourceType.key}
          entityLabel={label.toLowerCase()}
        />
      )}

      {absencesFor && (
        <ResourceAbsenceList
          open={!!absencesFor}
          onOpenChange={(open) => !open && setAbsencesFor(null)}
          resourceId={absencesFor.id}
          resourceName={absencesFor.name}
        />
      )}

      <ConfirmDialog
        open={!!removing}
        onOpenChange={(open) => !open && setRemoving(null)}
        title={`Deactivate "${removing?.name}"?`}
        description="It stops appearing in planning. Existing assignments are kept."
        confirmLabel="Deactivate"
        destructive
        isPending={deleteMutation.isPending}
        onConfirm={() => {
          if (removing) deleteMutation.mutate(removing.id);
        }}
      />
    </div>
  );
}
