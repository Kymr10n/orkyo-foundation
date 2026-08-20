import { Combobox } from '@foundation/src/components/ui/combobox';
import { useMemo } from 'react';
import { FormDialog } from '@foundation/src/components/ui/FormDialog';
import { Label } from '@foundation/src/components/ui/label';
import { ScalarValueInput } from '@foundation/src/components/fields/ScalarValueInput';
import { rowDisplayLabel } from '@foundation/src/components/lists/format-list-cell';
import { qk } from '@foundation/src/lib/api/query-keys';
import { useEntityFormDialog } from '@foundation/src/hooks/useEntityFormDialog';
import { useListRows } from '@foundation/src/hooks/useListRows';
import type { ListCellValue, ListColumn, ListRow } from '@foundation/src/lib/api/lists-api';

type RowValues = Record<string, ListCellValue>;

interface ListRowEditDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  /** The row being edited, or null to add one. */
  row: ListRow | null;
  columns: ListColumn[];
  instanceId: string | null;
  /** The definition's display column, which names the rows a `row_ref` column offers. */
  displayColumnId?: string | null;
  /** What one row is called, so the title says "Add Department" rather than "Add row". */
  entityLabel?: string;
  /**
   * Persists the row. The caller owns this because adding the first row to a per-resource list
   * has to create the instance first, which this dialog should not know about.
   */
  save: (values: RowValues, row: ListRow | null) => Promise<unknown>;
}

/**
 * Add or edit one row: a field per active column, in form order.
 *
 * The body is inline rather than a separate row-form component — it has exactly one consumer,
 * and a component with one call site is indirection without a payoff.
 */
export function ListRowEditDialog({
  open,
  onOpenChange,
  row,
  columns,
  instanceId,
  displayColumnId,
  entityLabel = 'row',
  save,
}: ListRowEditDialogProps) {
  const activeColumns = columns.filter((c) => c.isActive);

  // The rows a row_ref column can point at, which are this instance's own. Free: the editor
  // hosting this dialog already holds the same query, so this reads its cache.
  const { data: siblingRows = [] } = useListRows(instanceId);

  // Built once per row set rather than per keystroke: every character typed in any other field
  // re-renders this dialog, and rebuilding the option list each time is work no one asked for.
  const rowRefOptions = useMemo(
    () => [
      // Combobox has no clear control of its own, so emptying the cell is an entry.
      { id: '', label: 'None' },
      ...siblingRows
        .filter((sibling) => sibling.id !== row?.id)
        .map((sibling) => ({
          id: sibling.id,
          label: rowDisplayLabel(sibling, activeColumns, displayColumnId ?? null),
        })),
    ],
    [siblingRows, row?.id, activeColumns, displayColumnId],
  );

  const { form, set, isDirty, error, submit, isSubmitting } = useEntityFormDialog<
    ListRow,
    RowValues,
    unknown
  >({
    open,
    onOpenChange,
    entity: row,
    emptyForm: () => ({}),
    toForm: (entity) => ({ ...entity.values }),
    save: (values, entity) => save(values, entity),
    entityLabel: 'Row',
    // Required by the hook, and the same key the create/update mutations behind `save` declare.
    // React Query dedupes the pair, so the repetition costs a round trip to nobody.
    invalidates: [qk.lists.instanceRows(instanceId ?? 'none')],
  });

  // A required column with nothing in it is the one thing the dialog can check itself; every
  // other rule belongs to the server, which validates against the definition.
  const missingRequired = activeColumns.some(
    (column) =>
      column.isRequired &&
      (form[column.key] === null ||
        form[column.key] === undefined ||
        (typeof form[column.key] === 'string' && String(form[column.key]).trim() === '')),
  );

  return (
    <FormDialog
      open={open}
      onOpenChange={onOpenChange}
      title={row ? `Edit ${entityLabel}` : `Add ${entityLabel}`}
      error={error}
      onSubmit={submit}
      isSubmitting={isSubmitting}
      submitLabel={row ? 'Save' : 'Add'}
      submitDisabled={missingRequired}
      dirty={isDirty}
    >
      <div className="space-y-4">
        {activeColumns.map((column) => {
          const inputId = `list-cell-${column.key}`;
          const value = form[column.key] ?? null;

          // As on the resource form, a checkbox carries its own label and gets no label row.
          if (column.dataType === 'boolean') {
            return (
              <div key={column.key} className="space-y-2">
                <ScalarValueInput
                  id={inputId}
                  dataType="boolean"
                  value={value}
                  onChange={(next) => set({ [column.key]: next })}
                  required={column.isRequired}
                  label={column.label}
                />
                {column.description && (
                  <p className="text-muted-foreground text-xs">{column.description}</p>
                )}
              </div>
            );
          }

          const label = (
            <Label htmlFor={inputId}>
              {column.label}
              {column.isRequired && <span className="text-destructive ml-1">*</span>}
            </Label>
          );

          // A reference to another row of this same list. Every row but this one is offerable —
          // a longer loop is the server's to refuse, because only it can see the whole chain.
          if (column.dataType === 'row_ref') {
            return (
              <div key={column.key} className="space-y-2">
                {label}
                <Combobox
                  id={inputId}
                  value={typeof value === 'string' ? value : ''}
                  onChange={(id) => set({ [column.key]: id === '' ? null : id })}
                  options={rowRefOptions}
                  emptyText="No other rows yet"
                />
                {column.description && (
                  <p className="text-muted-foreground text-xs">{column.description}</p>
                )}
              </div>
            );
          }

          return (
            <div key={column.key} className="space-y-2">
              {label}
              <ScalarValueInput
                id={inputId}
                dataType={column.dataType}
                value={value}
                onChange={(next) => set({ [column.key]: next })}
                options={column.options}
                required={column.isRequired}
              />
              {column.description && (
                <p className="text-muted-foreground text-xs">{column.description}</p>
              )}
            </div>
          );
        })}

        {activeColumns.length === 0 && (
          <p className="text-muted-foreground text-sm">
            This list has no columns yet. Add one to its definition first.
          </p>
        )}
      </div>
    </FormDialog>
  );
}
