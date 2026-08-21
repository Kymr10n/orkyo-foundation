import { useMemo } from 'react';
import { Checkbox } from '@foundation/src/components/ui/checkbox';
import { Label } from '@foundation/src/components/ui/label';
import { useListRows } from '@foundation/src/hooks/useListRows';
import { useListDefinition } from '@foundation/src/hooks/useListDefinitions';
import {
  EMPTY_CELL,
  formatListCell,
  resolveDisplayColumn,
  rowDisplayLabel,
} from '@foundation/src/components/lists/format-list-cell';
import type { ListColumn, ListRow } from '@foundation/src/lib/api/lists-api';

interface ListRowPickerProps {
  /** The shared instance whose rows are on offer. */
  instanceId: string | null;
  /** The definition behind it, for column labels — fetched when not supplied. */
  definitionId?: string | null;
  value: string[];
  onChange: (rowIds: string[]) => void;
  disabled?: boolean;
}

/**
 * Picks rows out of a shared list.
 *
 * The value is row ids, not copies of the rows: that is what makes a shared list shared, so an
 * edit to a row is seen everywhere at once and a delete can take the id back out of every
 * resource that held it.
 *
 * A checkbox list rather than a multi-select combobox — the rows have several columns, and a
 * one-line trigger cannot show what a part actually is.
 */
export function ListRowPicker({
  instanceId,
  definitionId,
  value,
  onChange,
  disabled,
}: ListRowPickerProps) {
  const { data: rows = [], isLoading } = useListRows(instanceId);
  const { data: definition } = useListDefinition(definitionId ?? null);

  const activeColumns = useMemo(
    () => (definition?.columns ?? []).filter((c) => c.isActive),
    [definition],
  );

  const selected = new Set(value);

  const toggle = (rowId: string) => {
    const next = new Set(selected);
    if (next.has(rowId)) next.delete(rowId);
    else next.add(rowId);
    // Preserve the instance's own order rather than click order, so two resources with the same
    // picks store the same array and read the same way.
    onChange(rows.filter((row) => next.has(row.id)).map((row) => row.id));
  };

  if (isLoading) return <p className="text-muted-foreground text-sm">Loading options…</p>;

  if (rows.length === 0) {
    return (
      <p className="text-muted-foreground rounded-md border border-dashed p-3 text-sm">
        This list has no rows yet. An administrator adds them under Resources.
      </p>
    );
  }

  return (
    <div className="max-h-64 space-y-1 overflow-x-hidden overflow-y-auto rounded-md border p-2">
      {rows.map((row) => {
        const inputId = `list-pick-${row.id}`;
        return (
          <div key={row.id} className="flex items-start gap-2 rounded-sm p-1 hover:bg-muted/50">
            <Checkbox
              id={inputId}
              checked={selected.has(row.id)}
              onCheckedChange={() => toggle(row.id)}
              disabled={disabled}
              className="mt-0.5"
            />
            <Label
              htmlFor={inputId}
              className="min-w-0 cursor-pointer break-words text-sm font-normal"
            >
              {describeRow(row, activeColumns, definition?.displayColumnId ?? null)}
            </Label>
          </div>
        );
      })}
    </div>
  );
}

/**
 * A row as one line.
 *
 * The name comes from {@link rowDisplayLabel}, so a row reads the same here as in a cell that
 * points at it. What is added here is the tail: without a designated display column the name is
 * only a guess (the first active column), so the remaining columns follow as context and it reads
 * like a guess — "Name — 7'865". A designation is the author saying which field identifies the
 * row, and nothing is appended to that.
 */
function describeRow(row: ListRow, columns: ListColumn[], displayColumnId: string | null): string {
  if (columns.length === 0) return row.id;

  const head = rowDisplayLabel(row, columns, displayColumnId);

  // Only a designation suppresses the tail. A deactivated designated column is not in `columns`,
  // so the label fell back to the guess and the context belongs with it.
  const named = resolveDisplayColumn(columns, displayColumnId);
  if (displayColumnId && named?.id === displayColumnId) return head;

  // row_ref columns are skipped: their labels resolve against rows this function cannot see, so
  // they would contribute nothing but em dashes.
  const tail = columns
    .filter((column) => column !== named && column.dataType !== 'row_ref')
    .map((column) => formatListCell(column, row.values[column.key] ?? null))
    .filter((text) => text !== EMPTY_CELL);

  return tail.length > 0 ? `${head} — ${tail.join(' · ')}` : head;
}
