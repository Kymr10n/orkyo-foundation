import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ColumnDef } from '@tanstack/react-table';
import { format } from 'date-fns';

import { useAuditLogAvailable } from '@foundation/src/hooks/useAuditLogAvailable';
import { getTenantAuditEvents, type TenantAuditEvent } from '@foundation/src/lib/api/audit-api';
import { DATE_FORMATS } from '@foundation/src/lib/formatters';
import { OrkyoDataTable } from '@foundation/src/components/ui/OrkyoDataTable';
import { Card, CardContent, CardHeader, CardTitle } from '@foundation/src/components/ui/card';
import { FeatureUpsell } from '@foundation/src/components/ui/FeatureUpsell';

const PAGE_SIZE = 25;

/** Platform-sourced events (break-glass, staff tier/membership changes) carry this in metadata. */
interface AuditMetadata {
  source?: string;
  actorEmail?: string | null;
}

function parseMetadata(raw: string | null): AuditMetadata | null {
  if (!raw) return null;
  try {
    return JSON.parse(raw) as AuditMetadata;
  } catch {
    return null;
  }
}

interface AuditLogTabProps {
  /** Plans/upgrade link shown in the tier-gate upsell (SaaS). */
  upgradeHref?: string;
}

/**
 * Tenant-admin audit log (foundation → appears in SaaS + Community). Tier-gated to
 * Professional+ in SaaS via {@link useAuditLogAvailable}; always available in Community.
 * The page itself is already behind RequireTenantAdmin in TenantApp routing.
 */
export function AuditLogTab({ upgradeHref }: AuditLogTabProps = {}) {
  const available = useAuditLogAvailable();
  const [events, setEvents] = useState<TenantAuditEvent[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(0); // OrkyoDataTable is 0-indexed
  const [actionFilter, setActionFilter] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await getTenantAuditEvents({
        page: page + 1, // API is 1-based
        pageSize: PAGE_SIZE,
        action: actionFilter || undefined,
      });
      setEvents(res.events);
      setTotal(res.totalCount);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load audit log');
    } finally {
      setLoading(false);
    }
  }, [page, actionFilter]);

  useEffect(() => {
    if (available) void load();
  }, [available, load]);

  const columns = useMemo<ColumnDef<TenantAuditEvent>[]>(() => [
    {
      accessorKey: 'createdAt',
      header: 'When',
      cell: ({ row }) => (
        <span className="whitespace-nowrap text-sm text-muted-foreground">
          {format(new Date(row.original.createdAt), DATE_FORMATS.DATETIME_MEDIUM)}
        </span>
      ),
    },
    {
      id: 'actor',
      header: 'Actor',
      cell: ({ row }) => {
        const e = row.original;
        // Platform actions (site-admin break-glass, staff tier/membership changes) aren't tenant-DB
        // users; the actor's email is denormalized into metadata and the row is labeled.
        const meta = parseMetadata(e.metadata);
        if (meta?.source === 'platform') {
          return (
            <span className="flex items-center gap-2">
              <span>{meta.actorEmail || 'Orkyo Support'}</span>
              <span className="rounded bg-amber-100 px-1.5 py-0.5 text-xs font-medium text-amber-800 dark:bg-amber-950 dark:text-amber-300">
                Platform
              </span>
            </span>
          );
        }
        if (e.actorType !== 'user') return <span className="text-muted-foreground">System</span>;
        return <span>{e.actorDisplayName || e.actorEmail || 'Unknown user'}</span>;
      },
    },
    {
      accessorKey: 'action',
      header: 'Action',
      cell: ({ row }) => <span className="font-medium">{row.original.action}</span>,
    },
    {
      id: 'target',
      header: 'Target',
      cell: ({ row }) => {
        const e = row.original;
        if (!e.targetType) return <span className="text-muted-foreground">—</span>;
        return (
          <span className="text-sm">
            {e.targetType}
            {e.targetId ? <span className="text-muted-foreground"> · {e.targetId}</span> : null}
          </span>
        );
      },
    },
  ], []);

  if (!available) {
    return (
      <FeatureUpsell
        title="Audit Log"
        description="Available on Professional and Enterprise plans. Review who did what across your workspace — sign-ins, admin changes, and break-glass access."
        upgradeHref={upgradeHref}
      />
    );
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Audit Log</CardTitle>
      </CardHeader>
      <CardContent>
        <OrkyoDataTable
          columns={columns}
          data={events}
          isLoading={loading}
          error={error}
          emptyMessage="No audit events yet."
          filterValue={actionFilter}
          onFilterChange={(v) => { setPage(0); setActionFilter(v); }}
          filterPlaceholder="Filter by action…"
          pageSize={PAGE_SIZE}
          totalCount={total}
          page={page}
          onPageChange={setPage}
        />
      </CardContent>
    </Card>
  );
}
