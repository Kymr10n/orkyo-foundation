import { useState, useCallback } from 'react';
import {
  useReactTable,
  getCoreRowModel,
  getFilteredRowModel,
  getPaginationRowModel,
  flexRender,
  type ColumnDef,
  type ColumnFiltersState,
  type PaginationState,
  type RowData,
} from '@tanstack/react-table';
import { ChevronLeft, ChevronRight, Search } from 'lucide-react';
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

// Re-export so callers don't need a separate @tanstack/react-table import for ColumnDef
export type { ColumnDef, RowData };

export interface OrkyoDataTableProps<TData> {
  columns: ColumnDef<TData>[];
  data: TData[];
  isLoading?: boolean;
  error?: string | null;
  emptyMessage?: string;

  // Filtering — choose one mode:
  // Client-side: provide filterColumn (accessor key). Filter fires on keystroke.
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
}

export function OrkyoDataTable<TData>({
  columns,
  data,
  isLoading,
  error,
  emptyMessage = 'No results found.',
  filterColumn,
  filterPlaceholder = 'Search...',
  filterValue: controlledFilterValue,
  onFilterChange,
  filterOnSubmit,
  pageSize,
  totalCount,
  page: controlledPage,
  onPageChange,
}: OrkyoDataTableProps<TData>) {
  const isServerFilter = onFilterChange !== undefined;
  const isServerPagination = onPageChange !== undefined;

  // Local state for client-side filtering
  const [columnFilters, setColumnFilters] = useState<ColumnFiltersState>([]);
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

  const table = useReactTable({
    data,
    columns,
    getCoreRowModel: getCoreRowModel(),
    ...(filterColumn && !isServerFilter
      ? { getFilteredRowModel: getFilteredRowModel(), onColumnFiltersChange: setColumnFilters }
      : {}),
    ...(pageSize
      ? {
          getPaginationRowModel: getPaginationRowModel(),
          onPaginationChange: isServerPagination ? undefined : setPagination,
          manualPagination: isServerPagination,
          pageCount: serverPageCount,
        }
      : {}),
    state: {
      ...(filterColumn && !isServerFilter ? { columnFilters } : {}),
      ...(pageSize
        ? {
            pagination: isServerPagination
              ? { pageIndex: controlledPage ?? 0, pageSize: pageSize }
              : pagination,
          }
        : {}),
    },
    manualFiltering: isServerFilter,
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
        <div className="text-center py-8 text-muted-foreground">Loading...</div>
      ) : error ? (
        <div className="text-center py-8 text-destructive">{error}</div>
      ) : table.getRowModel().rows.length === 0 ? (
        <div className="text-center py-8 text-muted-foreground">{emptyMessage}</div>
      ) : (
        <div className="border rounded-md overflow-hidden">
          <Table>
            <TableHeader>
              {table.getHeaderGroups().map((hg) => (
                <TableRow key={hg.id}>
                  {hg.headers.map((header) => (
                    <TableHead key={header.id} style={{ width: header.getSize() !== 150 ? header.getSize() : undefined }}>
                      {header.isPlaceholder
                        ? null
                        : flexRender(header.column.columnDef.header, header.getContext())}
                    </TableHead>
                  ))}
                </TableRow>
              ))}
            </TableHeader>
            <TableBody>
              {table.getRowModel().rows.map((row) => (
                <TableRow key={row.id}>
                  {row.getVisibleCells().map((cell) => (
                    <TableCell key={cell.id}>
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {pageSize && !isLoading && !error && table.getRowModel().rows.length > 0 && (
        <div className="flex items-center justify-between px-1">
          <p className="text-sm text-muted-foreground">
            Page {currentPage + 1} of {pageCount}
          </p>
          <div className="flex gap-1">
            <Button variant="outline" size="icon" onClick={goToPrev} disabled={!canPrev}>
              <ChevronLeft className="h-4 w-4" />
            </Button>
            <Button variant="outline" size="icon" onClick={goToNext} disabled={!canNext}>
              <ChevronRight className="h-4 w-4" />
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
