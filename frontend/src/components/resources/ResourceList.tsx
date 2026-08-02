import { useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { Plus, Pencil, Trash2 } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { StatusBadge } from '@foundation/src/components/ui/status-badge';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { ResourceEditDialog } from './ResourceEditDialog';
import {
  deleteResource,
  getResources,
  type ResourceInfo,
} from '@foundation/src/lib/api/resources-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';


interface ResourceListProps {
  resourceType: ResourceTypeInfo;
}

/**
 * List/create/edit for any resource type, built from the type's own field definitions.
 * The first few custom fields become table columns so a type's defining details are
 * visible without opening each row.
 */
export function ResourceList({ resourceType }: ResourceListProps) {
  const canEdit = useCanEdit();
  const [editing, setEditing] = useState<ResourceInfo | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [removing, setRemoving] = useState<ResourceInfo | null>(null);


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

  const renderActions = (r: ResourceInfo) =>
    canEdit ? (
      <div className="flex justify-end gap-1">
        <Button
          variant="ghost"
          size="icon"
          onClick={() => setEditing(r)}
          aria-label={`Edit ${r.name}`}
        >
          <Pencil className="h-4 w-4" />
        </Button>
        <Button
          variant="ghost"
          size="icon"
          className="text-destructive hover:text-destructive"
          onClick={() => setRemoving(r)}
          aria-label={`Deactivate ${r.name}`}
        >
          <Trash2 className="h-4 w-4" />
        </Button>
      </div>
    ) : null;


  const columns: ColumnDef<ResourceInfo>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
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

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between">
        <div>
          <h2 className="text-lg font-semibold">{resourceType.displayName}</h2>
          {resourceType.description && (
            <p className="mt-1 text-sm text-muted-foreground">{resourceType.description}</p>
          )}
        </div>
        {canEdit && (
          <Button onClick={() => setCreateOpen(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Add {resourceType.displayName}
          </Button>
        )}
      </div>

      <OrkyoDataTable
        columns={columns}
        data={resources?.data ?? []}
        isLoading={isLoading}
        error={errorMsg}
        onRetry={() => refetch()}
        emptyMessage={`No ${resourceType.displayName.toLowerCase()} recorded yet.`}
        filterColumn="name"
        filterPlaceholder="Search..."
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
