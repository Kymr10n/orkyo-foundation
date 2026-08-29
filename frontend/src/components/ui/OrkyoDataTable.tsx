import { useState, useCallback, useMemo, type ReactNode } from 'react';
import { useTable, flexRender } from '@tanstack/react-table';
import type {
  ColumnFiltersState,
  OnChangeFn,
  PaginationState,
  RowData,
  SortingState,
} from '@tanstack/table-core';
import {
  orkyoTableFeatures,
  type Column,
  type ColumnDef,
} from '@foundation/src/lib/table/features';
import { AlertCircle, ChevronLeft, ChevronRight, Search } from 'lucide-react';
import { EmptyState } from '@foundation/src/components/ui/EmptyState';
import { Skeleton } from '@foundation/src/components/ui/skeleton';
import { Alert, AlertDescription } from '@foundation/src/components/ui/alert';
import { Input } from '@foundation/src/components/ui/input';
import { Button } from '@foundation/src/components/ui/button';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@foundation/src/components/ui/table';
import { DataTableColumnHeader } from '@foundation/src/components/ui/DataTableColumnHeader';
import { filterFnFor } from '@foundation/src/lib/table/column-meta';
import { useBreakpoint } from '@foundation/src/hooks/useBreakpoint';
import { cn } from '@foundation/src/lib/utils';

// Re-export so callers don't need a separate @tanstack/react-table import for ColumnDef
export type { ColumnDef, RowData };

function ariaSort<TData extends RowData>(column: Column<TData, unknown>): 'ascending' | 'descending' | undefined {
  const sorted = column.getIsSorted();
  return sorted === 'asc' ? 'ascending' : sorted === 'desc' ? 'descending' : undefined;
}

/** Plain-text column name for aria labels: meta.label, else the header string, else the id. */
function columnLabel<TData extends RowData>(column: Column<TData, unknown>): string {
  const def = column.columnDef;
  return def.meta?.label ?? (typeof def.header === 'string' ? def.header : column.id);
}

export interface OrkyoDataTableProps<TData extends RowData> {
  columns: ColumnDef<TData>[];
  data: TData[];
  isLoading?: boolean;
  error?: string | null;
  /** Shown as a "Try again" button next to the error alert. Omit to render no retry affordance. */
  onRetry?: () => void;
  emptyMessage?: string;
  /** Optional icon rendered above the empty message. */
  emptyIcon?: ReactNode;
  /** Optional CTA (e.g. a button) rendered below the empty message. */
  emptyAction?: ReactNode;

  // Filtering — choose one mode:
  // Client-side: provide filterColumn (accessor key). Filter fires on keystroke.
  // Foundation's own lists no longer use this — their text filters live in the column
  // headers (meta.filter) — but server-searched tables (SaaS admin) still render this box.
  filterColumn?: string;
  filterPlaceholder?: string;
  // Server-side: provide filterValue + onFilterChange. When filterOnSubmit is
  // true the filter only fires when the user presses the Search button (or Enter).
  filterValue?: string;
  onFilterChange?: (value: string) => void;
  filterOnSubmit?: boolean;

  // Pagination — omit pageSize to disable.
  // Client-side: pageSize only.
  // Server-side: pageSize + totalCount + page + onPageChange.
  pageSize?: number;
  totalCount?: number;
  page?: number;
  onPageChange?: (page: number) => void;

  // Column sorting / header filters — columns opt in via meta.filter (and sort by having an
  // accessor). Uncontrolled by default; pass these to own the state (useTableUrlState does,
  // to persist it in the URL). When the table is server-paged, providing them also switches
  // to manual mode: header menus only report state, and the call site queries the server.
  sorting?: SortingState;
  onSortingChange?: OnChangeFn<SortingState>;
  columnFilters?: ColumnFiltersState;
  onColumnFiltersChange?: OnChangeFn<ColumnFiltersState>;

  // Row interaction — when provided, rows become clickable (cursor + onClick).
  // Action-button cells must call e.stopPropagation() to avoid triggering this.
  onRowClick?: (row: TData) => void;

  // Responsive card mode — when provided, the table is replaced by a stacked
  // list of cards on phones (the grid is kept on tablet/desktop). Filtering,
  // pagination and onRowClick all still apply. Action buttons inside the card
  // must call e.stopPropagation() just like in a table cell.
  renderCard?: (row: TData) => ReactNode;
}

export function OrkyoDataTable<TData extends RowData>({
  columns,
  data,
  isLoading,
  error,
  onRetry,
  emptyMessage = 'No results found.',
  emptyIcon,
  emptyAction,
  filterColumn,
  filterPlaceholder = 'Search...',
  filterValue: controlledFilterValue,
  onFilterChange,
  filterOnSubmit,
  sorting: controlledSorting,
  onSortingChange,
  columnFilters: controlledColumnFilters,
  onColumnFiltersChange,
  pageSize,
  totalCount,
  page: controlledPage,
  onPageChange,
  onRowClick,
  renderCard,
}: OrkyoDataTableProps<TData>) {
  const isServerFilter = onFilterChange !== undefined;
  const isServerPagination = onPageChange !== undefined;
  const { isPhone } = useBreakpoint();
  const showCards = isPhone && renderCard !== undefined;

  // Local state for client-side filtering/sorting; controlled props take precedence.
  const [internalColumnFilters, setInternalColumnFilters] = useState<ColumnFiltersState>([]);
  const [internalSorting, setInternalSorting] = useState<SortingState>([]);
  const columnFilters = controlledColumnFilters ?? internalColumnFilters;
  const sorting = controlledSorting ?? internalSorting;
  const handleColumnFiltersChange = onColumnFiltersChange ?? setInternalColumnFilters;
  const handleSortingChange = onSortingChange ?? setInternalSorting;
  // Server-paged data is a window onto the real set, so sorting/filtering the visible rows
  // client-side would lie; with controlled state the call site translates to server params.
  const isManualTable = isServerPagination && onColumnFiltersChange !== undefined;
  // Pending value for button-press filter pattern
  const [pendingFilter, setPendingFilter] = useState(controlledFilterValue ?? '');

  // Client-side pagination state
  const [pagination, setPagination] = useState<PaginationState>({
    pageIndex: 0,
    pageSize: pageSize ?? 20,
  });

  const serverPageCount =
    isServerPagination && totalCount !== undefined && pageSize
      ? Math.ceil(totalCount / pageSize)
      : undefined;

  // Attach the filterFn each column's meta declares, so call sites never name one. The type
  // in the meta is the single source: it also drives which UI the header menu renders and how
  // the URL hook encodes the value.
  const resolvedColumns = useMemo(
    () =>
      columns.map((col) =>
        // The spread widens TanStack's discriminated ColumnDef union, so assert it back.
        col.meta?.filter && !col.filterFn
          ? ({ ...col, filterFn: filterFnFor(col.meta.filter) } as ColumnDef<TData>)
          : col,
      ),
    [columns],
  );

  const table = useTable({
    features: orkyoTableFeatures,
    data,
    columns: resolvedColumns,
    onColumnFiltersChange: handleColumnFiltersChange,
    onSortingChange: handleSortingChange,
    ...(pageSize
      ? {
          onPaginationChange: isServerPagination ? undefined : setPagination,
          manualPagination: isServerPagination,
          pageCount: serverPageCount,
        }
      : {}),
    state: {
      columnFilters,
      sorting,
      ...(pageSize
        ? {
            pagination: isServerPagination
              ? { pageIndex: controlledPage ?? 0, pageSize: pageSize }
              : pagination,
          }
        : {}),
    },
    manualFiltering: isServerFilter || isManualTable,
    manualSorting: isManualTable,
  });

  const handleClientFilterChange = useCallback(
    (value: string) => {
      if (!filterColumn) return;
      table.getColumn(filterColumn)?.setFilterValue(value);
    },
    [table, filterColumn],
  );

  const hasFilter = filterColumn !== undefined || isServerFilter;

  const currentPage = isServerPagination
    ? (controlledPage ?? 0)
    : pagination.pageIndex;
  const pageCount = isServerPagination
    ? (serverPageCount ?? 1)
    : table.getPageCount();

  const goToPrev = () => {
    if (isServerPagination) onPageChange(Math.max(0, currentPage - 1));
    else table.previousPage();
  };
  const goToNext = () => {
    if (isServerPagination) onPageChange(currentPage + 1);
    else table.nextPage();
  };

  const canPrev = currentPage > 0;
  const canNext = currentPage < pageCount - 1;

  return (
    <div className="space-y-3">
      {hasFilter && (
        <div className="flex gap-2">
          <Input
            placeholder={filterPlaceholder}
            value={
              isServerFilter
                ? filterOnSubmit
                  ? pendingFilter
                  : (controlledFilterValue ?? '')
                : (table.getColumn(filterColumn!)?.getFilterValue() as string) ?? ''
            }
            onChange={(e) => {
              const v = e.target.value;
              if (isServerFilter) {
                if (filterOnSubmit) {
                  setPendingFilter(v);
                } else {
                  onFilterChange(v);
                }
              } else {
                handleClientFilterChange(v);
              }
            }}
            onKeyDown={(e) => {
              if (e.key === 'Enter' && filterOnSubmit && isServerFilter) {
                onFilterChange(pendingFilter);
              }
            }}
            className="max-w-sm"
          />
          {filterOnSubmit && isServerFilter && (
            <Button
              variant="outline"
              size="default"
              onClick={() => onFilterChange(pendingFilter)}
            >
              <Search className="h-4 w-4 mr-2" />
              Search
            </Button>
          )}
        </div>
      )}

      {isLoading ? (
        <div role="status" aria-busy="true" aria-live="polite" className="space-y-1.5">
          <span className="sr-only">Loading…</span>
          {Array.from({ length: 5 }).map((_, r) => (
            <div
              key={r}
              aria-hidden="true"
              className="grid gap-4 rounded-lg border bg-card px-4 py-3.5"
              style={{ gridTemplateColumns: `repeat(${columns.length}, minmax(0, 1fr))` }}
            >
              {columns.map((_, c) => (
                <Skeleton key={c} className="h-4 w-full" />
              ))}
            </div>
          ))}
        </div>
      ) : error ? (
        <Alert variant="destructive">
          <AlertCircle className="h-4 w-4" />
          <AlertDescription className="flex items-center justify-between gap-2">
            <span>{error}</span>
            {onRetry && (
              <Button variant="outline" size="sm" onClick={onRetry}>
                Try again
              </Button>
            )}
          </AlertDescription>
        </Alert>
      ) : table.getRowModel().rows.length === 0 ? (
        <EmptyState message={emptyMessage} icon={emptyIcon} action={emptyAction} />
      ) : showCards ? (
        <div className="space-y-2">
          {table.getRowModel().rows.map((row) => (
            <div
              key={row.id}
              onClick={onRowClick ? () => onRowClick(row.original) : undefined}
              className={cn(
                'rounded-lg border bg-card p-3 shadow-xs',
                onRowClick && 'cursor-pointer hover:bg-accent/40',
              )}
            >
              {renderCard!(row.original)}
            </div>
          ))}
        </div>
      ) : (
        // No wrapper here: `Table` already brings its own scroll container. A second one
        // around it also computes `overflow-y` to `auto` (a `visible` axis does that whenever
        // the other axis is not visible), so it reserved vertical-scrollbar width and squeezed
        // the inner container into a horizontal scrollbar with nothing to scroll to.
        <Table className="border-separate border-spacing-y-1.5">
          <TableHeader>
            {table.getHeaderGroups().map((hg) => (
              <TableRow key={hg.id}>
                {hg.headers.map((header) => {
                  const column = header.column;
                  const interactive = !header.isPlaceholder
                    && (column.getCanSort() || column.columnDef.meta?.filter !== undefined);
                  return (
                    <TableHead
                      key={header.id}
                      aria-sort={ariaSort(column)}
                      style={{ width: header.getSize() !== 150 ? header.getSize() : undefined }}
                    >
                      {header.isPlaceholder ? null : interactive ? (
                        <DataTableColumnHeader
                          column={column}
                          title={flexRender(column.columnDef.header, header.getContext())}
                          label={columnLabel(column)}
                          facetsUnavailable={isManualTable}
                        />
                      ) : (
                        flexRender(column.columnDef.header, header.getContext())
                      )}
                    </TableHead>
                  );
                })}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody>
            {table.getRowModel().rows.map((row) => (
              <TableRow
                key={row.id}
                onClick={onRowClick ? () => onRowClick(row.original) : undefined}
                className={
                  'bg-card shadow-xs hover:bg-accent/40 [&>td:first-child]:rounded-l-lg [&>td:last-child]:rounded-r-lg [&>td]:border-y [&>td:first-child]:border-l [&>td:last-child]:border-r' +
                  (onRowClick ? ' cursor-pointer' : '')
                }
              >
                {row.getVisibleCells().map((cell) => (
                  <TableCell key={cell.id}>
                    {flexRender(cell.column.columnDef.cell, cell.getContext())}
                  </TableCell>
                ))}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}

      {pageSize && !isLoading && !error && table.getRowModel().rows.length > 0 && (
        <div className="flex items-center justify-between px-1">
          <p className="text-sm text-muted-foreground">
            Page {currentPage + 1} of {pageCount}
          </p>
          <div className="flex gap-1">
            <Button variant="outline" size="icon" onClick={goToPrev} disabled={!canPrev} aria-label="Previous page">
              <ChevronLeft className="h-4 w-4" />
            </Button>
            <Button variant="outline" size="icon" onClick={goToNext} disabled={!canNext} aria-label="Next page">
              <ChevronRight className="h-4 w-4" />
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
