import { formatLocalized } from '@foundation/src/lib/formatters';
import type { ListCellValue, ListColumn, ListRow } from '@foundation/src/lib/api/lists-api';

/** What an unfilled cell reads as. An em dash, not an empty string, so a gap is visibly a gap. */
export const EMPTY_CELL = '—';

/**
 * One cell as display text.
 *
 * Pure and shared: the table, the phone cards and the row picker all show the same cell, and a
 * date that reads one way in the grid and another in a picker is a bug nobody files but everyone
 * notices. Rendering (a link, a checkmark) belongs to the caller — this decides the words.
 *
 * `rowLabels` names the rows a `row_ref` cell can point at. A caller that has no such column need
 * not supply it, which is why it is last and optional.
 */
export function formatListCell(
  column: ListColumn,
  value: ListCellValue | undefined,
  rowLabels?: ReadonlyMap<string, string>,
): string {
  if (value === null || value === undefined) return EMPTY_CELL;

  switch (column.dataType) {
    case 'boolean':
      return value === true ? 'Yes' : 'No';

    case 'number':
      return typeof value === 'number' ? value.toLocaleString() : String(value);

    case 'date':
      // Dates arrive as yyyy-MM-dd, which is a plain date with no zone. Parsing it as a Date and
      // formatting would shift it a day for anyone west of UTC, so the parts are reordered
      // directly instead.
      return typeof value === 'string' ? formatIsoDate(value) : String(value);

    case 'row_ref':
      // A row id is not words. Unresolved — the row was deleted under us, or the caller passed no
      // labels — reads as a gap rather than as a UUID, which no reader can do anything with.
      return (typeof value === 'string' ? rowLabels?.get(value) : undefined) ?? EMPTY_CELL;

    default:
      return typeof value === 'string' && value.trim().length > 0 ? value : EMPTY_CELL;
  }
}

/**
 * A row as the one value that names it.
 *
 * When the definition designates a display column, that column alone names the row — the author
 * said which field identifies it. Without a designation the first active column is used, which is
 * a guess; it exists so definitions predating the designation still render.
 *
 * Shared by everything that shows a row as a single thing: the picker's label, a row_ref cell, the
 * options in a row_ref combobox. One row read the same way everywhere.
 */
export function rowDisplayLabel(
  row: ListRow,
  columns: ListColumn[],
  displayColumnId: string | null,
): string {
  const named = resolveDisplayColumn(columns, displayColumnId);
  return named ? formatListCell(named, row.values[named.key] ?? null) : row.id;
}

/**
 * Which column names a row: the designated one, else the first that can name anything.
 *
 * Shared with the picker, which needs to know whether the name came from a designation (nothing
 * is appended to it) or from the fallback guess (the other columns follow as context).
 *
 * A `row_ref` is never it. Naming a row by a reference would need the labels of the rows it points
 * at — which are themselves named by this column — so it renders as an empty cell and takes every
 * row on the list with it, the one being pointed at included. Returns undefined when no column
 * qualifies, and the caller falls back to the id.
 */
export function resolveDisplayColumn(
  columns: ListColumn[],
  displayColumnId: string | null,
): ListColumn | undefined {
  // A designated column that has since been deactivated is not in `columns`, so this falls
  // through to the first active one rather than naming the row by a field the form no longer asks
  // for.
  const designated = displayColumnId
    ? columns.find((column) => column.id === displayColumnId && column.dataType !== 'row_ref')
    : undefined;

  return designated ?? columns.find((column) => column.dataType !== 'row_ref');
}

/**
 * A date column holds a plain yyyy-MM-dd — a calendar day, with no time and no zone.
 *
 * `formatDateDisplay` is not used for it: that parses with `new Date(str)`, which reads a bare
 * date as UTC midnight and then renders it locally, so anyone west of UTC sees the day before.
 * Building the Date from its parts keeps it the day the tenant typed, and formatLocalized still
 * respects their locale.
 */
function formatIsoDate(value: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!match) return value;

  const [, year, month, day] = match;
  return formatLocalized(new Date(Number(year), Number(month) - 1, Number(day)), {
    dateStyle: 'medium',
  });
}
