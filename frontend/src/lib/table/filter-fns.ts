import type { FilterFn } from '@tanstack/react-table';

/**
 * Custom column filter functions for OrkyoDataTable, registered once there and referenced by
 * name from the column-meta mapping — call sites never name a filterFn. TanStack ships
 * `includesString` (text) and `inNumberRange` (number); these cover what it lacks.
 */

/** Facet filter for scalar cells: the row passes when its value is one of the checked ones. */
export const oneOf: FilterFn<unknown> = (row, columnId, filterValue: string[]) =>
  filterValue.includes(String(row.getValue(columnId)));
// An empty facet selection means "no filter", not "match nothing" — without autoRemove an
// empty array would blank the table the moment the last checkbox is unticked.
oneOf.autoRemove = (value) => !Array.isArray(value) || value.length === 0;

/** Facet filter for array-valued cells (e.g. a criterion's applicable types): overlap, not equality. */
export const arrayOverlaps: FilterFn<unknown> = (row, columnId, filterValue: string[]) => {
  const cell = row.getValue<string[] | null>(columnId);
  return Array.isArray(cell) && cell.some((v) => filterValue.includes(v));
};
arrayOverlaps.autoRemove = (value) => !Array.isArray(value) || value.length === 0;

/**
 * Date-range filter over cells holding ISO strings or epoch ms. Either bound may be open.
 * A cell that does not parse (null date, e.g. "never logged in") is excluded from range
 * results — asking for a range is asking for rows that have a date in it.
 */
export const dateBetween: FilterFn<unknown> = (row, columnId, filterValue: [string?, string?]) => {
  const [lo, hi] = filterValue;
  const t = new Date(row.getValue<string | number>(columnId)).getTime();
  if (Number.isNaN(t)) return false;
  // The upper bound is a date, not an instant: "to 2026-06-30" must include that whole day.
  return (
    (!lo || t >= new Date(lo).getTime()) &&
    (!hi || t <= new Date(hi).getTime() + 86_399_999)
  );
};
dateBetween.autoRemove = (value) =>
  !Array.isArray(value) || (value[0] === undefined && value[1] === undefined);

/**
 * Facet options for array-valued columns. TanStack's faceting treats the whole array cell as
 * one Map key, so a column of ["space","person"] cells would offer "space,person" as a single
 * option; this flattens to per-element counts.
 */
export function flattenFacets(faceted: Map<unknown, number>): Map<string, number> {
  const flat = new Map<string, number>();
  for (const [key, count] of faceted) {
    const values = Array.isArray(key) ? key : [key];
    for (const v of values) {
      const s = String(v);
      flat.set(s, (flat.get(s) ?? 0) + count);
    }
  }
  return flat;
}

declare module '@tanstack/react-table' {
  interface FilterFns {
    oneOf: FilterFn<unknown>;
    arrayOverlaps: FilterFn<unknown>;
    dateBetween: FilterFn<unknown>;
  }
}
