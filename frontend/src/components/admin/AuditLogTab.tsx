import { useCallback, useEffect, useMemo, useState } from 'react';
import type { ColumnDef } from '@foundation/src/lib/table/features';
import { format } from 'date-fns';

import { useTableUrlState } from '@foundation/src/hooks/useTableUrlState';

import { useAuditLogAvailable } from '@foundation/src/hooks/useAuditLogAvailable';
import { getTenantAuditEvents, type TenantAuditEvent } from '@foundation/src/lib/api/audit-api';
import { DATE_FORMATS } from '@foundation/src/lib/formatters';
import { OrkyoDataTable } from '@foundation/src/components/ui/OrkyoDataTable';
import { Card, CardContent, CardHeader, CardTitle } from '@foundation/src/components/ui/card';
import { Badge } from '@foundation/src/components/ui/badge';
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
  const [loading, setLoading] = useState(true);
  const [loadedOnce, setLoadedOnce] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Server-mode headers: this table is a window onto thousands of rows, so filtering the
  // visible page client-side would lie. Header filters only report state; the query below
  // translates them to API params. Sorting stays off — the server's order is fixed (newest
  // first) and offering a sort that silently reorders one page would mislead.
  const columns = useMemo<ColumnDef<TenantAuditEvent>[]>(() => [
    {
      accessorKey: 'createdAt',
      header: 'When',
      enableSorting: false,
      meta: { filter: { type: 'date' } },
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
              <Badge variant="warning">Platform</Badge>
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
      enableSorting: false,
      meta: { filter: { type: 'text' } },
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

  const urlState = useTableUrlState('audit', columns);
  const { columnFilters } = urlState;

  // Translate the header-filter state into the API's params. Kept as one memo so the load
  // effect and the page-reset effect key on the same value.
  const apiFilters = useMemo(() => {
    const action = columnFilters.find((f) => f.id === 'action')?.value as string | undefined;
    const range = columnFilters.find((f) => f.id === 'createdAt')?.value as
      | [string?, string?]
      | undefined;
    return { action: action || undefined, from: range?.[0], to: range?.[1] };
  }, [columnFilters]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await getTenantAuditEvents({
        page: page + 1, // API is 1-based
        pageSize: PAGE_SIZE,
        ...apiFilters,
      });
      setEvents(res.events);
      setTotal(res.totalCount);
      setLoadedOnce(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load audit log');
    } finally {
      setLoading(false);
    }
  }, [page, apiFilters]);

  useEffect(() => {
    // Manual load by design on this operator surface — see docs/dialog-feedback.md.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (available) void load();
  }, [available, load]);

  // A page number only means something within one filtered set. Render-phase, not an effect:
  // an effect would first issue one request for the stale page (see useEntityFormDialog.ts).
  const filterSignature = JSON.stringify(apiFilters);
  const [syncedSignature, setSyncedSignature] = useState(filterSignature);
  if (syncedSignature !== filterSignature) {
    setSyncedSignature(filterSignature);
    setPage(0);
  }

  // Phone presentation: action + actor/target stacked, timestamp last. Read-only, no actions.
  const renderCard = (e: TenantAuditEvent) => {
    const meta = parseMetadata(e.metadata);
    const isPlatform = meta?.source === 'platform';
    const actorLabel = isPlatform
      ? meta?.actorEmail || 'Orkyo Support'
      : e.actorType !== 'user'
        ? 'System'
        : e.actorDisplayName || e.actorEmail || 'Unknown user';
    return (
      <div className="min-w-0 space-y-1">
        <div className="flex items-center gap-2 min-w-0">
          <span className="font-medium truncate">{e.action}</span>
          {isPlatform && <Badge variant="warning" className="shrink-0">Platform</Badge>}
        </div>
        <p className="text-sm text-muted-foreground truncate">{actorLabel}</p>
        <p className="text-xs text-muted-foreground truncate">
          {e.targetType ? `${e.targetType}${e.targetId ? ` · ${e.targetId}` : ''} · ` : ''}
          {format(new Date(e.createdAt), DATE_FORMATS.DATETIME_MEDIUM)}
        </p>
      </div>
    );
  };

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
          // Skeletons only before anything has loaded. A header-filter keystroke refetches,
          // and swapping the table for skeletons would unmount the open filter popover under
          // the user's cursor; stale rows for a beat are the lesser evil.
          isLoading={loading && !loadedOnce}
          error={error}
          onRetry={() => void load()}
          emptyMessage="No audit events yet."
          {...urlState}
          pageSize={PAGE_SIZE}
          totalCount={total}
          page={page}
          onPageChange={setPage}
          renderCard={renderCard}
        />
      </CardContent>
    </Card>
  );
}
