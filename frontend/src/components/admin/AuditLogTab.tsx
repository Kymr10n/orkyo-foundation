import { useMemo } from 'react';
import { format } from 'date-fns';

import { useTableUrlState } from '@foundation/src/hooks/useTableUrlState';

import { FeatureKeys } from '@foundation/contracts/plans';
import { useFeatureEnabled } from '@foundation/src/hooks/useFeatureEnabled';
import { getTenantAuditEvents, type TenantAuditEvent } from '@foundation/src/lib/api/audit-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import { DATE_FORMATS } from '@foundation/src/lib/formatters';
import type { ColumnDef } from '@foundation/src/components/ui/OrkyoDataTable';
import { Badge } from '@foundation/src/components/ui/badge';
import { FeatureUpsell } from '@foundation/src/components/ui/FeatureUpsell';
import { AuditEventsTable } from '@foundation/src/components/admin/AuditEventsTable';

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
 * Professional+ in SaaS via `useFeatureEnabled(FeatureKeys.AuditLog)`; always available in Community.
 * The page itself is already behind RequireTenantAdmin in TenantApp routing.
 */
export function AuditLogTab({ upgradeHref }: AuditLogTabProps = {}) {
  const available = useFeatureEnabled(FeatureKeys.AuditLog);

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
  const { columnFilters, ...tableProps } = urlState;

  // Translate the header-filter state into the API's params. One memo, so it is also the
  // value the table's page reset keys on.
  const filters = useMemo(() => {
    const action = columnFilters.find((f) => f.id === 'action')?.value as string | undefined;
    const range = columnFilters.find((f) => f.id === 'createdAt')?.value as
      | [string?, string?]
      | undefined;
    return { action: action || undefined, from: range?.[0], to: range?.[1] };
  }, [columnFilters]);

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
    <AuditEventsTable
      title="Audit Log"
      columns={columns}
      filters={filters}
      queryKey={(page, f, size) => [...qk.audit.events(page, f), size]}
      fetchPage={({ page, pageSize, filters: f }) =>
        getTenantAuditEvents({ page, pageSize, ...f })
      }
      renderCard={renderCard}
      tableProps={{ ...tableProps, columnFilters }}
    />
  );
}
