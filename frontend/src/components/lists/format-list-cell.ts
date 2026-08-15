import { formatLocalized } from '@foundation/src/lib/formatters';
import type { ListCellValue, ListColumn } from '@foundation/src/lib/api/lists-api';

/** What an unfilled cell reads as. An em dash, not an empty string, so a gap is visibly a gap. */
export const EMPTY_CELL = '—';

/**
 * One cell as display text.
 *
 * Pure and shared: the table, the phone cards and the row picker all show the same cell, and a
 * date that reads one way in the grid and another in a picker is a bug nobody files but everyone
 * notices. Rendering (a link, a checkmark) belongs to the caller — this decides the words.
 */
export function formatListCell(column: ListColumn, value: ListCellValue | undefined): string {
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

    default:
      return typeof value === 'string' && value.trim().length > 0 ? value : EMPTY_CELL;
  }
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
