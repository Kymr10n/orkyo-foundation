import { type ReactNode, useState } from 'react';

import { useAuditEventPage, type AuditEventPage } from '@foundation/src/hooks/usePlatformAdmin';
import {
  OrkyoDataTable,
  type ColumnDef,
  type OrkyoDataTableProps,
  type RowData,
} from '@foundation/src/components/ui/OrkyoDataTable';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@foundation/src/components/ui/card';

export type { AuditEventPage };

/** The table props each log wires differently: one uses header filters, the other a search box. */
type AuditTableFilterProps<T extends RowData> = Pick<
  OrkyoDataTableProps<T>,
  | 'filterValue'
  | 'onFilterChange'
  | 'filterPlaceholder'
  | 'filterOnSubmit'
  | 'sorting'
  | 'onSortingChange'
  | 'columnFilters'
  | 'onColumnFiltersChange'
>;

interface AuditEventsTableProps<T extends RowData, F> {
  title: ReactNode;
  description?: ReactNode;
  columns: ColumnDef<T>[];
  /** Filter state that scopes the query. Any change resets the table to the first page. */
  filters: F;
  /**
   * React Query key for one page. `page` is 0-based, as the table is.
   *
   * `pageSize` is passed too, and must be part of the key: two callers of this component at
   * different page sizes would otherwise collide on one cache entry and read each other's rows.
   */
  queryKey: (page: number, filters: F, pageSize: number) => readonly unknown[];
  /** Fetches one page. `page` is 1-based, as both audit APIs are. */
  fetchPage: (args: { page: number; pageSize: number; filters: F }) => Promise<AuditEventPage<T>>;
  renderCard: (row: T) => ReactNode;
  emptyMessage?: string;
  pageSize?: number;
  tableProps?: AuditTableFilterProps<T>;
}

const DEFAULT_PAGE_SIZE = 25;

/**
 * Paginated audit-log table. The fetcher, the columns and the filter wiring are props; the
 * paging is owned here.
 *
 * One consumer today, the tenant log (`/api/audit`). It is generic because the control-plane
 * log (`/api/admin/audit`) is the same surface over a different endpoint and column set, and
 * adopts this in the hosted product once foundation publishes. Until then, read the generic
 * prop surface as a foundation library component with one caller, not as shared code.
 */
export function AuditEventsTable<T extends RowData, F>({
  title,
  description,
  columns,
  filters,
  queryKey,
  fetchPage,
  renderCard,
  emptyMessage = 'No audit events yet.',
  pageSize = DEFAULT_PAGE_SIZE,
  tableProps,
}: AuditEventsTableProps<T, F>) {
  const [page, setPage] = useState(0); // OrkyoDataTable is 0-indexed

  // A page number only means something within one filtered set. Render-phase, not an effect:
  // an effect would first issue one request for the stale page (see useEntityFormDialog.ts).
  const filterSignature = JSON.stringify(filters);
  const [syncedSignature, setSyncedSignature] = useState(filterSignature);
  if (syncedSignature !== filterSignature) {
    setSyncedSignature(filterSignature);
    setPage(0);
  }

  const query = useAuditEventPage(queryKey(page, filters, pageSize), () =>
    fetchPage({ page: page + 1, pageSize, filters }),
  );

  return (
    <Card>
      <CardHeader>
        <CardTitle>{title}</CardTitle>
        {description ? <CardDescription>{description}</CardDescription> : null}
      </CardHeader>
      <CardContent>
        <OrkyoDataTable
          columns={columns}
          data={query.data?.events ?? []}
          isLoading={query.isLoading}
          error={query.error instanceof Error ? query.error.message : null}
          onRetry={() => void query.refetch()}
          emptyMessage={emptyMessage}
          {...tableProps}
          pageSize={pageSize}
          totalCount={query.data?.totalCount ?? 0}
          page={page}
          onPageChange={setPage}
          renderCard={renderCard}
        />
      </CardContent>
    </Card>
  );
}
