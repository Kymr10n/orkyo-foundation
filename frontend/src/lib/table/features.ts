import {
  tableFeatures,
  columnFacetingFeature,
  columnFilteringFeature,
  columnSizingFeature,
  columnVisibilityFeature,
  rowPaginationFeature,
  rowSortingFeature,
  createCoreRowModel,
  createFacetedRowModel,
  createFacetedUniqueValues,
  createFilteredRowModel,
  createPaginatedRowModel,
  createSortedRowModel,
  filterFn_includesString,
  filterFn_inNumberRange,
} from '@tanstack/table-core';
import type {
  Column as TanStackColumn,
  ColumnDef as TanStackColumnDef,
  Row as TanStackRow,
  RowData,
  Table as TanStackTable,
} from '@tanstack/react-table';
import { arrayOverlaps, dateBetween, oneOf } from '@foundation/src/lib/table/filter-fns';

/**
 * The feature set, row models and filter functions every Orkyo table uses.
 *
 * TanStack Table v9 is modular: a table only carries the methods of the features it
 * declares, every table type takes that feature set as its first generic, and the row
 * models and custom filter functions are registered in the same object. Declaring it once
 * here — rather than per call site — keeps every table in the product on the same
 * capabilities and gives the aliases below a single source for their first generic.
 *
 * Add a feature here only when a table needs it; each one costs bundle size and state.
 */
export const orkyoTableFeatures = tableFeatures({
  columnFacetingFeature,
  columnFilteringFeature,
  columnSizingFeature,
  columnVisibilityFeature,
  rowPaginationFeature,
  rowSortingFeature,

  coreRowModel: createCoreRowModel(),
  filteredRowModel: createFilteredRowModel(),
  sortedRowModel: createSortedRowModel(),
  paginatedRowModel: createPaginatedRowModel(),
  facetedRowModel: createFacetedRowModel(),
  facetedUniqueValues: createFacetedUniqueValues(),

  // Registered here so call sites name a filter through column meta and never import a fn.
  // v9 registers built-ins explicitly too — column meta names includesString and
  // inNumberRange, so they have to be in the slot alongside the custom fns.
  filterFns: {
    oneOf,
    arrayOverlaps,
    dateBetween,
    includesString: filterFn_includesString,
    inNumberRange: filterFn_inNumberRange,
  },
});

export type OrkyoTableFeatures = typeof orkyoTableFeatures;

/**
 * Aliases that bind the feature generic, so call sites write `ColumnDef<Person>` and not
 * `ColumnDef<OrkyoTableFeatures, Person>`. Import these instead of the TanStack types.
 */
export type ColumnDef<TData extends RowData, TValue = unknown> = TanStackColumnDef<
  OrkyoTableFeatures,
  TData,
  TValue
>;
export type Column<TData extends RowData, TValue = unknown> = TanStackColumn<
  OrkyoTableFeatures,
  TData,
  TValue
>;
export type Row<TData extends RowData> = TanStackRow<OrkyoTableFeatures, TData>;
export type Table<TData extends RowData> = TanStackTable<OrkyoTableFeatures, TData>;
