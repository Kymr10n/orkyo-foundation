import { useState } from 'react';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { Badge } from '@foundation/src/components/ui/badge';
import { StatusBadge } from '@foundation/src/components/ui/status-badge';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { ScaffoldDialog } from '@foundation/src/components/ui/ScaffoldDialog';
import { ListColumnEditDialog } from './ListColumnEditDialog';
import { useDeleteListColumn, useListDefinition } from '@foundation/src/hooks/useListDefinitions';
import { listColumnDataTypeLabel, type ListColumn } from '@foundation/src/lib/api/lists-api';

interface ListColumnsDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  definitionId: string;
  definitionName: string;
}

/**
 * The columns of one definition, in form order.
 *
 * A ScaffoldDialog rather than a FormDialog: there is no single form to submit here — each
 * column is its own edit, and the list of them is the content.
 */
export function ListColumnsDialog({
  open,
  onOpenChange,
  definitionId,
  definitionName,
}: ListColumnsDialogProps) {
  const { data: definition, isLoading } = useListDefinition(open ? definitionId : null);
  const deleteColumn = useDeleteListColumn(definitionId);

  const [editing, setEditing] = useState<ListColumn | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [removing, setRemoving] = useState<ListColumn | null>(null);

  const columns = definition?.columns ?? [];

  return (
    <ScaffoldDialog
      open={open}
      onOpenChange={onOpenChange}
      title={`Columns — ${definitionName}`}
      description="The fields each row of this list has. Order here is the order of the row form."
    >
      <div className="space-y-4">
        <div className="flex justify-end">
          <Button size="sm" onClick={() => setCreateOpen(true)}>
            <Plus className="mr-2 h-4 w-4" />
            Add column
          </Button>
        </div>

        {isLoading && <p className="text-muted-foreground text-sm">Loading columns…</p>}

        {!isLoading && columns.length === 0 && (
          <p className="text-muted-foreground rounded-md border border-dashed p-4 text-sm">
            No columns yet. A list with no columns has nothing to record — add the first one.
          </p>
        )}

        <ul className="divide-y rounded-md border">
          {columns.map((column) => (
            <li key={column.id} className="flex items-start justify-between gap-3 p-3">
              <div className="min-w-0 space-y-1">
                <div className="flex flex-wrap items-center gap-2">
                  <span className="font-medium">{column.label}</span>
                  <Badge variant="secondary">{listColumnDataTypeLabel(column.dataType)}</Badge>
                  {column.isRequired && <Badge variant="outline">Required</Badge>}
                  {!column.isActive && <StatusBadge status="inactive" label="Inactive" />}
                </div>
                <p className="text-muted-foreground truncate text-xs">
                  {column.description || column.key}
                </p>
                {column.dataType === 'select' && column.options && column.options.length > 0 && (
                  <p className="text-muted-foreground truncate text-xs">
                    Options: {column.options.join(', ')}
                  </p>
                )}
              </div>
              <div className="flex shrink-0 gap-1">
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={`Edit ${column.label}`}
                  onClick={() => setEditing(column)}
                >
                  <Pencil className="h-4 w-4" />
                </Button>
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={`Remove ${column.label}`}
                  onClick={() => setRemoving(column)}
                >
                  <Trash2 className="text-destructive h-4 w-4" />
                </Button>
              </div>
            </li>
          ))}
        </ul>
      </div>

      <ListColumnEditDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        definitionId={definitionId}
        column={null}
      />

      {editing && (
        <ListColumnEditDialog
          open={editing !== null}
          onOpenChange={(open) => !open && setEditing(null)}
          definitionId={definitionId}
          column={editing}
        />
      )}

      <ConfirmDialog
        open={removing !== null}
        onOpenChange={(open) => !open && setRemoving(null)}
        title={`Remove ${removing?.label ?? 'this column'}?`}
        // Says what actually happens: the cells go with the column, in the same transaction.
        description="Every row of every list built from this definition loses what it recorded in this column. Deactivate it instead to keep the data and drop it from the form."
        confirmLabel="Remove"
        destructive
        onConfirm={async () => {
          if (removing) await deleteColumn.mutateAsync(removing.id);
          setRemoving(null);
        }}
      />
    </ScaffoldDialog>
  );
}
