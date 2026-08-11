import { useState } from 'react';
import { Plus, Pencil, Trash2 } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { Badge } from '@foundation/src/components/ui/badge';
import { StatusBadge } from '@foundation/src/components/ui/status-badge';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { RowActions } from '@foundation/src/components/ui/RowActions';
import { Separator } from '@foundation/src/components/ui/separator';
import { DialogFooter, ScrollableDialogBody } from '@foundation/src/components/ui/dialog';
import { ScaffoldDialog } from '@foundation/src/components/ui/ScaffoldDialog';
import { OrkyoDataTable, type ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { CustomFieldEditDialog } from './CustomFieldEditDialog';
import {
  useDeleteResourceCustomField,
  useResourceCustomFields,
} from '@foundation/src/hooks/useResourceCustomFields';
import {
  customFieldDataTypeLabel,
  type ResourceCustomField,
} from '@foundation/src/lib/api/resource-custom-fields-api';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';

interface ResourceTypeCustomFieldsDialogProps {
  resourceType: ResourceTypeInfo;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

/**
 * The descriptive fields one resource type puts on its resources.
 *
 * A dialog opened from the type's row, matching how every other parent-to-children
 * collection is edited here (absences, capabilities, skills): one detail surface per
 * entity, reached from the list that owns it.
 */
export function ResourceTypeCustomFieldsDialog({
  resourceType,
  open,
  onOpenChange,
}: ResourceTypeCustomFieldsDialogProps) {
  const {
    data: fields = [],
    isLoading,
    error,
    refetch,
  } = useResourceCustomFields(resourceType.id, open);
  const deleteField = useDeleteResourceCustomField(resourceType.id);

  const [editing, setEditing] = useState<ResourceCustomField | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [removing, setRemoving] = useState<ResourceCustomField | null>(null);

  const renderActions = (field: ResourceCustomField) => (
    <RowActions
      triggerLabel={`Actions for ${field.label}`}
      actions={[
        { label: 'Edit', icon: Pencil, onSelect: () => setEditing(field) },
        { label: 'Remove', icon: Trash2, onSelect: () => setRemoving(field), destructive: true },
      ]}
    />
  );

  const columns: ColumnDef<ResourceCustomField>[] = [
    {
      accessorKey: 'label',
      header: 'Field',
      cell: ({ row }) => (
        <div className="min-w-0">
          <span className="font-medium">{row.original.label}</span>
          <p className="text-muted-foreground truncate text-sm">
            {row.original.description || row.original.key}
          </p>
        </div>
      ),
    },
    {
      accessorKey: 'dataType',
      header: 'Type',
      cell: ({ row }) => (
        <Badge variant="secondary">{customFieldDataTypeLabel(row.original.dataType)}</Badge>
      ),
    },
    {
      id: 'shown',
      header: 'On the form',
      cell: ({ row }) => (
        <div className="flex flex-wrap gap-1">
          {row.original.isRequired && <Badge variant="outline">Required</Badge>}
          {!row.original.isActive && <StatusBadge status="inactive" label="Hidden" />}
          {row.original.isActive && !row.original.isRequired && (
            <span className="text-muted-foreground">—</span>
          )}
        </div>
      ),
    },
    {
      id: 'actions',
      header: () => <span className="sr-only">Actions</span>,
      size: 60,
      cell: ({ row }) => renderActions(row.original),
    },
  ];

  const renderCard = (field: ResourceCustomField) => (
    <div className="flex items-start justify-between gap-2">
      <div className="min-w-0 space-y-1">
        <div className="flex items-center gap-2">
          <span className="font-medium truncate">{field.label}</span>
          <Badge variant="secondary">{customFieldDataTypeLabel(field.dataType)}</Badge>
          {field.isRequired && <Badge variant="outline">Required</Badge>}
          {!field.isActive && <StatusBadge status="inactive" label="Hidden" />}
        </div>
        <p className="text-sm text-muted-foreground truncate">
          {field.description || field.key}
        </p>
      </div>
      {renderActions(field)}
    </div>
  );

  const errorMsg =
    error instanceof Error ? error.message : error ? 'Failed to load custom fields' : null;

  return (
    <>
      <ScaffoldDialog
        open={open}
        onOpenChange={onOpenChange}
        size="xl"
        title={`Custom fields — ${resourceType.displayNamePlural}`}
        description={`Descriptive properties shown on the ${resourceType.displayName.toLowerCase()} form — a serial number, a link to the datasheet. They are not used for matching: a value the scheduler should reason over belongs in Criteria.`}
      >
        <ScrollableDialogBody className="space-y-4 px-6 pb-4">
          <div className="flex justify-end">
            <Button size="sm" onClick={() => setCreateOpen(true)}>
              <Plus className="h-4 w-4 mr-2" />
              Add custom field
            </Button>
          </div>

          <OrkyoDataTable
            columns={columns}
            data={fields}
            isLoading={isLoading}
            error={errorMsg}
            onRetry={() => refetch()}
            emptyMessage="No custom fields yet."
            renderCard={renderCard}
          />
        </ScrollableDialogBody>

        <Separator />

        <DialogFooter className="px-6 pb-6 pt-4">
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Close
          </Button>
        </DialogFooter>
      </ScaffoldDialog>

      <CustomFieldEditDialog
        resourceTypeId={resourceType.id}
        field={null}
        open={createOpen}
        onOpenChange={setCreateOpen}
      />
      {editing && (
        <CustomFieldEditDialog
          resourceTypeId={resourceType.id}
          field={editing}
          open={!!editing}
          onOpenChange={(isOpen) => !isOpen && setEditing(null)}
        />
      )}

      <ConfirmDialog
        open={!!removing}
        onOpenChange={(isOpen) => !isOpen && setRemoving(null)}
        title={`Remove "${removing?.label}"?`}
        description="The values resources hold for this field are discarded with it. To stop asking for the field but keep what has already been captured, uncheck “Shown on the form” instead."
        confirmLabel="Remove"
        destructive
        isPending={deleteField.isPending}
        onConfirm={() => {
          if (!removing) return;
          deleteField.mutate(removing.id, { onSuccess: () => setRemoving(null) });
        }}
      />
    </>
  );
}
