import { useCallback, useState, type ReactNode } from 'react';
import { Pencil, Plus, Trash2 } from 'lucide-react';
import { Button } from '@foundation/src/components/ui/button';
import { ConfirmDialog } from '@foundation/src/components/ui/ConfirmDialog';
import { ListRowEditDialog } from '@foundation/src/components/lists/ListRowEditDialog';
import { ListRowsTable } from '@foundation/src/components/lists/ListRowsTable';
import {
  useCreateListRow,
  useDeleteListRow,
  useListRows,
  useUpdateListRow,
} from '@foundation/src/hooks/useListRows';
import type { ListCellValue, ListColumn, ListRow } from '@foundation/src/lib/api/lists-api';

interface ListRowsEditorProps {
  columns: ListColumn[];
  /** Null while the list has no instance yet — the table renders empty and add still works. */
  instanceId: string | null;
  /**
   * The definition's display column. Names a row wherever one is shown as a single value, which
   * is what a `row_ref` cell and its picker show.
   */
  displayColumnId?: string | null;
  /**
   * Creates the instance on demand and returns its id. Supplied for a per-resource list, where
   * the holder is made on the first row rather than when the form opens; omitted for a shared
   * instance, which always exists before its rows do.
   */
  ensureInstanceId?: () => Promise<string>;
  /** Hides every write affordance — for a viewer, or a resource being created. */
  readOnly?: boolean;
  emptyMessage?: string;
  /**
   * Rendered in the action row beside the Add button, so a host joins the one toolbar instead of
   * stacking a second row of chrome above it — the shared-list panel puts its selector here.
   */
  toolbar?: ReactNode;
  /**
   * What one row is called — "department", "Job Title", "Maintenance Log". Names the Add button,
   * the dialog titles and the delete confirmation, so the surface says what it acts on rather
   * than the generic "row".
   */
  entityLabel?: string;
}

/**
 * The whole row-editing surface: the table, the add/edit dialog and the delete confirmation.
 *
 * One component for both kinds of list. The admin data grid renders it against a shared instance
 * and the resource form renders it against a per-resource one; the only difference is
 * `ensureInstanceId`, which exists precisely so the resource form can defer creating the holder
 * until someone actually adds a row.
 */
export function ListRowsEditor({
  columns,
  instanceId,
  displayColumnId,
  ensureInstanceId,
  readOnly,
  emptyMessage,
  toolbar,
  entityLabel = 'row',
}: ListRowsEditorProps) {
  const [editing, setEditing] = useState<ListRow | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [pendingDelete, setPendingDelete] = useState<ListRow | null>(null);
  // Set once the holder has been created here. The prop catches up when its query refetches, but
  // this is what the mutations bind to in the meantime.
  const [createdInstanceId, setCreatedInstanceId] = useState<string | null>(null);
  const [isPreparing, setIsPreparing] = useState(false);

  const effectiveInstanceId = instanceId ?? createdInstanceId;

  const { data: rows, isLoading, error } = useListRows(effectiveInstanceId);
  const createRow = useCreateListRow(effectiveInstanceId);
  const updateRow = useUpdateListRow(effectiveInstanceId);
  const deleteRow = useDeleteListRow(effectiveInstanceId);

  /**
   * The holder is created before the dialog opens, not when the row is saved: the mutations bind
   * their instance id at render, so a dialog opened against a null id would post the row to a
   * URL with no instance in it.
   */
  const openAddDialog = async () => {
    if (!effectiveInstanceId && ensureInstanceId) {
      setIsPreparing(true);
      try {
        setCreatedInstanceId(await ensureInstanceId());
      } finally {
        setIsPreparing(false);
      }
    }
    setEditing(null);
    setDialogOpen(true);
  };

  const saveRow = (values: Record<string, ListCellValue>, row: ListRow | null) =>
    row
      ? updateRow.mutateAsync({ rowId: row.id, request: { values } })
      : createRow.mutateAsync({ values });

  // Stable across renders on purpose: the table memoizes its column definitions on this, and an
  // inline arrow here would hand it a new identity every render — the memo would never hold, and
  // the columns would be rebuilt on every keystroke anywhere in the page. The setters are stable,
  // so the closure only has to follow the label and the read-only flag.
  const renderRowActions = useCallback(
    (row: ListRow) => (
      <div className="flex justify-end gap-1">
        <Button
          type="button"
          variant="ghost"
          size="icon"
          aria-label={`Edit ${entityLabel}`}
          onClick={(e) => {
            // The row itself may be clickable in a host that wires onRowClick.
            e.stopPropagation();
            setEditing(row);
            setDialogOpen(true);
          }}
        >
          <Pencil className="h-4 w-4" />
        </Button>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          aria-label={`Delete ${entityLabel}`}
          onClick={(e) => {
            e.stopPropagation();
            setPendingDelete(row);
          }}
        >
          <Trash2 className="text-destructive h-4 w-4" />
        </Button>
      </div>
    ),
    [entityLabel],
  );

  return (
    <div className="space-y-3">
      {(!readOnly || toolbar) && (
        <div className="flex items-center justify-end gap-2">
          {toolbar}
          {!readOnly && (
            <Button type="button" onClick={openAddDialog} disabled={isPreparing}>
              <Plus className="mr-2 h-4 w-4" />
              Add {entityLabel}
            </Button>
          )}
        </div>
      )}

      <ListRowsTable
        columns={columns}
        rows={rows ?? []}
        displayColumnId={displayColumnId}
        isLoading={isLoading}
        error={error ? 'Failed to load rows' : null}
        emptyMessage={emptyMessage}
        renderRowActions={readOnly ? undefined : renderRowActions}
      />

      {dialogOpen && (
        <ListRowEditDialog
          open={dialogOpen}
          onOpenChange={setDialogOpen}
          row={editing}
          columns={columns}
          instanceId={effectiveInstanceId}
          displayColumnId={displayColumnId}
          entityLabel={entityLabel}
          save={saveRow}
        />
      )}

      <ConfirmDialog
        open={pendingDelete !== null}
        onOpenChange={(open) => !open && setPendingDelete(null)}
        title={`Delete this ${entityLabel.toLowerCase()}?`}
        description="The row is removed from this list. Anything that selected it drops the reference."
        confirmLabel="Delete"
        destructive
        onConfirm={async () => {
          if (pendingDelete) await deleteRow.mutateAsync(pendingDelete.id);
          setPendingDelete(null);
        }}
      />
    </div>
  );
}
