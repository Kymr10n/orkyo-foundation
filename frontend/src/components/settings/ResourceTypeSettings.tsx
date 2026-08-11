import { useState } from 'react';
import { Plus, Pencil, Trash2, ListPlus } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { Badge } from '@foundation/src/components/ui/badge';
import { StatusBadge } from '@foundation/src/components/ui/status-badge';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { RowActions } from '@foundation/src/components/ui/RowActions';
import { SettingsPageHeader } from './SettingsPageHeader';
import { ResourceTypeEditDialog } from './ResourceTypeEditDialog';
import { ResourceTypeCustomFieldsDialog } from './ResourceTypeCustomFieldsDialog';
import {
  useDeleteResourceType,
  useResourceTypes,
} from '@foundation/src/hooks/useResourceTypes';
import { useTableUrlState } from '@foundation/src/hooks/useTableUrlState';
import { resourceTypeIcon } from '@foundation/src/components/resources/resource-type-icon';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';

/**
 * No role gating inside this page: it is mounted only under RequireTenantAdmin, and the API
 * behind it is admin-write, so everyone who can see it can use all of it.
 */
export function ResourceTypeSettings() {
  const { data: types = [], isLoading, error, refetch } = useResourceTypes();
  const deleteType = useDeleteResourceType();

  const [editing, setEditing] = useState<ResourceTypeInfo | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [removing, setRemoving] = useState<ResourceTypeInfo | null>(null);
  const [managingFields, setManagingFields] = useState<ResourceTypeInfo | null>(null);

  // Shared by the desktop table cell and the phone card. Custom fields are offered on every
  // type, built-in ones included: a serial number on a Tool is as ordinary as one on a Car,
  // even though a system type's own name and behaviour stay locked.
  const renderActions = (type: ResourceTypeInfo) => (
    <RowActions
      triggerLabel={`Actions for ${type.displayName}`}
      actions={[
        { label: 'Custom fields', icon: ListPlus, onSelect: () => setManagingFields(type) },
        ...(type.isSystem
          ? []
          : [
              { label: 'Edit', icon: Pencil, onSelect: () => setEditing(type) },
              { label: 'Remove', icon: Trash2, onSelect: () => setRemoving(type), destructive: true },
            ]),
      ]}
    />
  );

  const columns: ColumnDef<ResourceTypeInfo>[] = [
    {
      accessorKey: 'displayName',
      header: 'Name',
      meta: { filter: { type: 'text' } },
      cell: ({ row }) => {
        const Icon = resourceTypeIcon(row.original.icon);
        return (
          <div className="flex items-center gap-2">
            <Icon className="h-4 w-4 shrink-0 text-muted-foreground" />
            <span className="font-semibold">{row.original.displayName}</span>
            {row.original.isSystem && <Badge variant="secondary">Built-in</Badge>}
          </div>
        );
      },
    },
    {
      accessorKey: 'description',
      header: 'Description',
      meta: { filter: { type: 'text' } },
      cell: ({ row }) => (
        <span className="text-muted-foreground text-sm">
          {row.original.description || row.original.key}
        </span>
      ),
    },
    {
      id: 'status',
      accessorFn: (type) => (type.isActive ? 'Active' : 'Inactive'),
      header: 'Status',
      meta: { filter: { type: 'enum' } },
      cell: ({ row }) =>
        row.original.isActive ? (
          <StatusBadge status="active" label="Active" />
        ) : (
          <StatusBadge status="inactive" label="Inactive" />
        ),
    },
    {
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      size: 60,
      cell: ({ row }) => renderActions(row.original),
    },
  ];

  const renderCard = (type: ResourceTypeInfo) => {
    const Icon = resourceTypeIcon(type.icon);
    return (
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0 space-y-1">
          <div className="flex items-center gap-2">
            <Icon className="h-4 w-4 shrink-0 text-muted-foreground" />
            <span className="font-semibold truncate">{type.displayName}</span>
            {type.isSystem && <Badge variant="secondary">Built-in</Badge>}
            {!type.isActive && <StatusBadge status="inactive" label="Inactive" />}
          </div>
          <p className="text-sm text-muted-foreground truncate">
            {type.description || type.key}
          </p>
        </div>
        {renderActions(type)}
      </div>
    );
  };

  const errorMsg =
    error instanceof Error ? error.message : error ? 'Failed to load resource types' : null;

  // Header sort/filter state lives in the URL: bookmarkable, shareable, Back-safe.
  const tableUrlState = useTableUrlState('resource-types', columns);

  return (
    <div className="space-y-6">
      <SettingsPageHeader
        title="Resource Types"
        description="The kinds of things your organization manages. Spaces, people, and tools are built in; add your own — cars, cameras, anything — and give each the custom fields it needs."
      >
        <Button onClick={() => setCreateOpen(true)}>
          <Plus className="h-4 w-4 mr-2" />
          Add Resource Type
        </Button>
      </SettingsPageHeader>

      <OrkyoDataTable
        {...tableUrlState}
        columns={columns}
        data={types}
        isLoading={isLoading}
        error={errorMsg}
        onRetry={() => refetch()}
        emptyMessage="No resource types defined yet."
        renderCard={renderCard}
      />

      <ResourceTypeEditDialog resourceType={null} open={createOpen} onOpenChange={setCreateOpen} />
      {editing && (
        <ResourceTypeEditDialog
          resourceType={editing}
          open={!!editing}
          onOpenChange={(open) => !open && setEditing(null)}
        />
      )}

      {managingFields && (
        <ResourceTypeCustomFieldsDialog
          resourceType={managingFields}
          open={!!managingFields}
          onOpenChange={(open) => !open && setManagingFields(null)}
        />
      )}

      <ConfirmDialog
        open={!!removing}
        onOpenChange={(open) => !open && setRemoving(null)}
        title={`Remove "${removing?.displayName}"?`}
        description="If resources of this type already exist, the type is deactivated instead of deleted so those resources keep working."
        confirmLabel="Remove"
        destructive
        isPending={deleteType.isPending}
        onConfirm={() => {
          if (!removing) return;
          deleteType.mutate(removing.id, { onSuccess: () => setRemoving(null) });
        }}
      />
    </div>
  );
}
