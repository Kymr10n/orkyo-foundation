import { useMemo, type ReactNode } from 'react';
import { OrkyoDataTable } from '@foundation/src/components/ui/OrkyoDataTable';
import type { ColumnDef } from '@foundation/src/lib/table/features';
import type { ListColumn, ListRow } from '@foundation/src/lib/api/lists-api';
import {
  EMPTY_CELL,
  formatListCell,
  rowDisplayLabel,
} from '@foundation/src/components/lists/format-list-cell';

interface ListRowsTableProps {
  columns: ListColumn[];
  rows: ListRow[];
  isLoading?: boolean;
  error?: string | null;
  emptyMessage?: string;
  /** The definition's display column, which names a row a `row_ref` cell points at. */
  displayColumnId?: string | null;
  /** Per-row controls (edit, delete). Rendered in a trailing column when provided. */
  renderRowActions?: (row: ListRow) => ReactNode;
}

/**
 * A list instance's rows, as a table whose columns come from the definition rather than from code.
 *
 * Pure presentation: it holds no queries and no dialogs, so the same table serves the admin data
 * grid and the per-resource editor on a resource form.
 *
 * Three things follow from OrkyoDataTable being on TanStack Table v9, and are why the column
 * builder looks the way it does:
 *   - columns are typed as the `ColumnDef` alias from lib/table/features, never the raw TanStack
 *     type, so the feature generic lines up;
 *   - filters are declared only through `meta.filter`, and the filter functions themselves are
 *     registered centrally — a column may name one, never pass one;
 *   - `ColumnFilterMeta` has no boolean case, so yes/no columns declare no filter. They still
 *     sort, which is the useful half.
 */
export function ListRowsTable({
  columns,
  rows,
  isLoading,
  error,
  emptyMessage = 'No rows yet.',
  displayColumnId,
  renderRowActions,
}: ListRowsTableProps) {
  const activeColumns = useMemo(() => columns.filter((c) => c.isActive), [columns]);

  // A row_ref points at a row of this same instance, so the rows already on screen are the whole
  // universe of targets — no second query names them.
  const rowLabels = useMemo(
    () =>
      new Map(rows.map((row) => [row.id, rowDisplayLabel(row, activeColumns, displayColumnId ?? null)])),
    [rows, activeColumns, displayColumnId],
  );

  const tableColumns = useMemo<ColumnDef<ListRow>[]>(() => {
    const defs: ColumnDef<ListRow>[] = activeColumns.map((column) => ({
      id: column.key,
      // A row_ref sorts and filters by the words shown, not by the id stored: nobody searches a
      // department list for a UUID.
      accessorFn: (row: ListRow) =>
        column.dataType === 'row_ref'
          ? formatListCell(column, row.values[column.key] ?? null, rowLabels)
          : (row.values[column.key] ?? null),
      header: column.label,
      meta: {
        label: column.label,
        // select → enum (the oneOf filter fn is registered centrally); text/url/row_ref → text;
        // date → date; number → number; boolean → nothing, see the note above.
        ...(column.dataType === 'select'
          ? { filter: { type: 'enum' as const } }
          : column.dataType === 'date'
            ? { filter: { type: 'date' as const } }
            : column.dataType === 'number'
              ? { filter: { type: 'number' as const } }
              : column.dataType === 'boolean'
                ? {}
                : { filter: { type: 'text' as const } }),
      },
      cell: ({ row, getValue }) => {
        const value = row.original.values[column.key] ?? null;
        // The accessor already resolved a reference to its label; formatting it twice per cell
        // would just repeat a map lookup and a switch.
        if (column.dataType === 'row_ref') return getValue() as string;
        if (column.dataType === 'url' && typeof value === 'string' && value.length > 0) {
          return (
            <a
              href={value}
              target="_blank"
              rel="noopener noreferrer"
              className="text-primary underline underline-offset-2"
              onClick={(e) => e.stopPropagation()}
            >
              {value}
            </a>
          );
        }
        return formatListCell(column, value);
      },
    }));

    if (renderRowActions) {
      defs.push({
        id: 'actions',
        header: '',
        enableSorting: false,
        cell: ({ row }) => renderRowActions(row.original),
      });
    }

    return defs;
  }, [activeColumns, rowLabels, renderRowActions]);

  return (
    <OrkyoDataTable
      columns={tableColumns}
      data={rows}
      isLoading={isLoading}
      error={error}
      emptyMessage={emptyMessage}
      // On a phone the grid becomes stacked cards: every column as a label/value pair, so a row
      // stays readable without horizontal scrolling.
      renderCard={(row) => (
        <div className="space-y-1">
          {activeColumns.map((column) => (
            <div key={column.key} className="flex justify-between gap-3 text-sm">
              <span className="text-muted-foreground">{column.label}</span>
              <span className="text-right">
                {formatListCell(column, row.values[column.key] ?? null, rowLabels) || EMPTY_CELL}
              </span>
            </div>
          ))}
          {renderRowActions && <div className="flex justify-end pt-1">{renderRowActions(row)}</div>}
        </div>
      )}
    />
  );
}
