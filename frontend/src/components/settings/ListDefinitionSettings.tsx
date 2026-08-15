import { useState } from 'react';
import { Columns3, Pencil, Plus, Rows3, Trash2 } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { StatusBadge } from '@foundation/src/components/ui/status-badge';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { RowActions } from '@foundation/src/components/ui/RowActions';
import { SettingsPageHeader } from './SettingsPageHeader';
import { ListDefinitionEditDialog } from './ListDefinitionEditDialog';
import { ListColumnsDialog } from './ListColumnsDialog';
import { ListInstancesDialog } from './ListInstancesDialog';
import {
  useDeleteListDefinition,
  useListDefinitions,
} from '@foundation/src/hooks/useListDefinitions';
import { useTableUrlState } from '@foundation/src/hooks/useTableUrlState';
import type { ListDefinition } from '@foundation/src/lib/api/lists-api';

/**
 * The list definitions a tenant has defined: the shapes its lists take.
 *
 * No role gating inside the page — it is mounted only under RequireTenantAdmin and the API
 * behind it is admin-write, so everyone who can see it can use all of it.
 */
export function ListDefinitionSettings() {
  const { data: definitions = [], isLoading, error } = useListDefinitions(true);
  const deleteDefinition = useDeleteListDefinition();

  const [editing, setEditing] = useState<ListDefinition | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [managingColumns, setManagingColumns] = useState<ListDefinition | null>(null);
  const [managingInstances, setManagingInstances] = useState<ListDefinition | null>(null);
  const [removing, setRemoving] = useState<ListDefinition | null>(null);

  const renderActions = (definition: ListDefinition) => (
    <RowActions
      triggerLabel={`Actions for ${definition.name}`}
      actions={[
        { label: 'Columns', icon: Columns3, onSelect: () => setManagingColumns(definition) },
        { label: 'Shared lists', icon: Rows3, onSelect: () => setManagingInstances(definition) },
        { label: 'Edit', icon: Pencil, onSelect: () => setEditing(definition) },
        {
          label: 'Remove',
          icon: Trash2,
          onSelect: () => setRemoving(definition),
          destructive: true,
        },
      ]}
    />
  );

  const columns: ColumnDef<ListDefinition>[] = [
    {
      accessorKey: 'name',
      header: 'Name',
      meta: { filter: { type: 'text' } },
      cell: ({ row }) => (
        <div className="flex items-center gap-2">
          <span className="font-semibold">{row.original.name}</span>
          {!row.original.isActive && <StatusBadge status="inactive" label="Inactive" />}
        </div>
      ),
    },
    {
      accessorKey: 'description',
      header: 'Description',
      meta: { filter: { type: 'text' } },
      cell: ({ row }) => (
        <span className="text-muted-foreground">{row.original.description || '—'}</span>
      ),
    },
    {
      id: 'actions',
      header: '',
      enableSorting: false,
      cell: ({ row }) => <div className="flex justify-end">{renderActions(row.original)}</div>,
    },
  ];

  const errorMsg =
    error instanceof Error ? error.message : error ? 'Failed to load list definitions' : null;

  // Header sort/filter state lives in the URL: bookmarkable, shareable, Back-safe.
  const tableUrlState = useTableUrlState('list-definitions', columns);

  return (
    <div className="space-y-6">
      <SettingsPageHeader
        title="List definitions"
        description="The shapes your lists take. A definition sets the columns — a maintenance log's date and mileage, a parts list's number and price — and any resource type can then carry a list of that shape."
      >
        <Button onClick={() => setCreateOpen(true)}>
          <Plus className="mr-2 h-4 w-4" />
          Add list definition
        </Button>
      </SettingsPageHeader>

      <OrkyoDataTable
        {...tableUrlState}
        columns={columns}
        data={definitions}
        isLoading={isLoading}
        error={errorMsg}
        emptyMessage="No list definitions yet."
        renderCard={(definition) => (
          <div className="flex items-start justify-between gap-2">
            <div className="min-w-0 space-y-1">
              <div className="flex items-center gap-2">
                <span className="truncate font-semibold">{definition.name}</span>
                {!definition.isActive && <StatusBadge status="inactive" label="Inactive" />}
              </div>
              <p className="text-muted-foreground truncate text-sm">
                {definition.description || '—'}
              </p>
            </div>
            {renderActions(definition)}
          </div>
        )}
      />

      <ListDefinitionEditDialog open={createOpen} onOpenChange={setCreateOpen} definition={null} />

      {editing && (
        <ListDefinitionEditDialog
          open={editing !== null}
          onOpenChange={(open) => !open && setEditing(null)}
          definition={editing}
        />
      )}

      {managingColumns && (
        <ListColumnsDialog
          open={managingColumns !== null}
          onOpenChange={(open) => !open && setManagingColumns(null)}
          definitionId={managingColumns.id}
          definitionName={managingColumns.name}
        />
      )}

      {managingInstances && (
        <ListInstancesDialog
          open={managingInstances !== null}
          onOpenChange={(open) => !open && setManagingInstances(null)}
          definitionId={managingInstances.id}
          definitionName={managingInstances.name}
        />
      )}

      <ConfirmDialog
        open={removing !== null}
        onOpenChange={(open) => !open && setRemoving(null)}
        title={`Delete ${removing?.name ?? 'this definition'}?`}
        // A definition still in use is refused by the API with a 409 rather than cascading, so
        // the honest thing to say is what will happen, not "this cannot be undone".
        description="This is refused while any field or shared list still uses this shape. Deactivate it instead to stop new fields from using it."
        confirmLabel="Delete"
        destructive
        onConfirm={async () => {
          if (removing) await deleteDefinition.mutateAsync(removing.id);
          setRemoving(null);
        }}
      />
    </div>
  );
}
