import type { FilterFnOption, RowData } from '@tanstack/react-table';

/**
 * The declaration a column makes to opt into header filtering. Sorting needs no declaration —
 * any column with an accessor is sortable (TanStack's default), which automatically excludes
 * display-only columns like `actions`; opt out per column with `enableSorting: false`.
 *
 * The type drives everything downstream: which filter UI the header menu renders, which
 * filterFn the table attaches, and how useTableUrlState encodes the value — one declaration,
 * three consumers.
 */
export type ColumnFilterMeta =
  | { type: 'text' }
  | {
      type: 'enum';
      /**
       * Maps a raw cell value to the label the facet list shows. Point it at the same lookup
       * the badge cell uses, so the menu offers exactly what the column displays while URLs
       * stay stable on raw values.
       */
      getLabel?: (value: string) => string;
      /** The cell holds an array of values (facets flatten per element, matching is overlap). */
      isArray?: boolean;
    }
  | { type: 'date' }
  | { type: 'number' };

declare module '@tanstack/react-table' {
  // TanStack declares these generics on ColumnMeta; the merge must repeat them verbatim.
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  interface ColumnMeta<TData extends RowData, TValue> {
    filter?: ColumnFilterMeta;
    /** Accessible name when `header` is not a plain string (aria-labels, menu heading). */
    label?: string;
  }
}

/** The filterFn each meta type resolves to. Names registered in OrkyoDataTable's filterFns. */
export function filterFnFor(meta: ColumnFilterMeta): FilterFnOption<never> {
  switch (meta.type) {
    case 'text':
      return 'includesString';
    case 'enum':
      return meta.isArray ? 'arrayOverlaps' : 'oneOf';
    case 'date':
      return 'dateBetween';
    case 'number':
      return 'inNumberRange';
  }
}
