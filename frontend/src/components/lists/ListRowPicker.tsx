import { useMemo } from 'react';
import { Checkbox } from '@foundation/src/components/ui/checkbox';
import { Label } from '@foundation/src/components/ui/label';
import { useListRows } from '@foundation/src/hooks/useListRows';
import { useListDefinition } from '@foundation/src/hooks/useListDefinitions';
import { formatListCell } from '@foundation/src/components/lists/format-list-cell';
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
    <div className="max-h-64 space-y-1 overflow-y-auto rounded-md border p-2">
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
            <Label htmlFor={inputId} className="cursor-pointer text-sm font-normal">
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
 * When the definition designates a display column, that column alone names the row — the author
 * said which field identifies it, so nothing else is appended. Without a designation the first
 * active column leads and the rest follow as context, which is a guess and reads like one
 * ("Name — 7'865"); that fallback exists so definitions predating the designation still render.
 *
 * Uses the same formatter the table does, so a date reads identically whether it is being picked
 * or being displayed.
 */
function describeRow(row: ListRow, columns: ListColumn[], displayColumnId: string | null): string {
  if (columns.length === 0) return row.id;

  // A designated column that has since been deactivated is not in `columns`, so this falls
  // through to the guess rather than naming the row by a field the form no longer asks for.
  const designated = displayColumnId
    ? columns.find((column) => column.id === displayColumnId)
    : undefined;

  if (designated) return formatListCell(designated, row.values[designated.key] ?? null);

  const [primary, ...rest] = columns;
  const head = formatListCell(primary, row.values[primary.key] ?? null);
  const tail = rest
    .map((column) => formatListCell(column, row.values[column.key] ?? null))
    .filter((text) => text !== '—');

  return tail.length > 0 ? `${head} — ${tail.join(' · ')}` : head;
}
