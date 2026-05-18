import { useState } from 'react';
import { useQueries, useQuery } from '@tanstack/react-query';
import { ChevronDown, ChevronRight } from 'lucide-react';
import { getResources, type ResourceInfo } from '@foundation/src/lib/api/resources-api';
import {
  getResourceUtilization,
  type ResourceUtilizationBucket,
} from '@foundation/src/lib/api/resource-utilization-api';
import {
  startOfDay,
  startOfWeek,
  startOfMonth,
  addDays,
  addMonths,
  format,
} from 'date-fns';

// ── Types ────────────────────────────────────────────────────────────────────

type TimeScale = 'year' | 'month' | 'week' | 'day' | 'hour';

export interface PeopleUtilizationGridProps {
  anchorTs: Date;
  scale: TimeScale;
}

interface ViewWindow {
  from: Date;
  to: Date;
  granularity: string;
}

// ── Helpers ──────────────────────────────────────────────────────────────────

function getViewWindow(anchorTs: Date, scale: TimeScale): ViewWindow {
  switch (scale) {
    case 'year':
      return {
        from: startOfMonth(new Date(anchorTs.getFullYear(), 0, 1)),
        to: addMonths(startOfMonth(new Date(anchorTs.getFullYear(), 0, 1)), 12),
        granularity: 'week',
      };
    case 'week': {
      const ws = startOfWeek(anchorTs, { weekStartsOn: 1 });
      return { from: ws, to: addDays(ws, 7), granularity: 'day' };
    }
    case 'day':
      return {
        from: startOfDay(anchorTs),
        to: addDays(startOfDay(anchorTs), 1),
        granularity: 'hour',
      };
    case 'month':
    default:
      return {
        from: startOfMonth(anchorTs),
        to: addMonths(startOfMonth(anchorTs), 1),
        granularity: 'day',
      };
  }
}

function bucketLabel(bucket: ResourceUtilizationBucket, granularity: string): string {
  const d = new Date(bucket.start);
  switch (granularity) {
    case 'week': return format(d, 'MMM d');
    case 'day':  return format(d, 'MMM d');
    case 'hour': return format(d, 'HH:mm');
    default:     return format(d, 'MMM d');
  }
}

type BucketStatus = 'available' | 'partial' | 'assigned' | 'overbooked' | 'non-working';

function bucketStatus(bucket: ResourceUtilizationBucket): BucketStatus {
  if (bucket.effectiveAvailabilityPercent === 0) return 'non-working';
  if (bucket.isExclusiveOccupied)               return 'assigned';
  if (bucket.allocatedPercent === 0)             return 'available';
  if (bucket.allocatedPercent >= bucket.effectiveAvailabilityPercent) return 'overbooked';
  return 'partial';
}

// Design-system token classes — semantic names, not arbitrary hex colors.
const STATUS_CELL_CLASS: Record<BucketStatus, string> = {
  available:     'bg-emerald-100/60 dark:bg-emerald-950/40',
  partial:       'bg-amber-100/60 dark:bg-amber-950/40',
  assigned:      'bg-blue-100/60 dark:bg-blue-950/40',
  overbooked:    'bg-red-100/60 dark:bg-red-950/40',
  'non-working': 'bg-muted/40',
};

// ── Component ────────────────────────────────────────────────────────────────

export function PeopleUtilizationGrid({ anchorTs, scale }: PeopleUtilizationGridProps) {
  const [isCollapsed, setIsCollapsed] = useState(false);

  const { from, to, granularity } = getViewWindow(anchorTs, scale);

  // 1. Fetch all active person resources
  const { data: peopleResponse, isLoading: peopleLoading } = useQuery({
    queryKey: ['resources', 'person', 'utilization-grid'],
    queryFn: () => getResources({ resourceTypeKey: 'person', isActive: true }),
  });
  const people: ResourceInfo[] = peopleResponse?.data ?? [];

  // 2. Fetch per-person utilization in parallel
  const utilQueries = useQueries({
    queries: people.map((p) => ({
      queryKey: ['resource-utilization', p.id, from.toISOString(), to.toISOString(), granularity],
      queryFn: () => getResourceUtilization(p.id, from, to, granularity),
      staleTime: 60_000,
    })),
  });

  // Don't render the section at all until we know whether any people exist
  if (peopleLoading) return null;
  if (people.length === 0) return null;

  // Build column headers from the first person's buckets (all should be the same shape)
  const firstBuckets = utilQueries[0]?.data?.buckets ?? [];

  return (
    <div className="border-b bg-card" data-testid="people-utilization-grid">
      {/* Section header */}
      <button
        type="button"
        className="w-full flex items-center gap-2 px-4 py-2 text-sm font-medium text-left hover:bg-accent/50 transition-colors"
        onClick={() => setIsCollapsed((c) => !c)}
        data-testid="people-grid-toggle"
      >
        {isCollapsed ? (
          <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" />
        ) : (
          <ChevronDown className="h-4 w-4 shrink-0 text-muted-foreground" />
        )}
        <span>People ({people.length})</span>
        {!isCollapsed && (
          <div className="ml-auto flex items-center gap-3 text-xs text-muted-foreground font-normal">
            <span className="flex items-center gap-1">
              <span className="inline-block h-2 w-4 rounded-sm bg-emerald-100 border border-emerald-200" /> Available
            </span>
            <span className="flex items-center gap-1">
              <span className="inline-block h-2 w-4 rounded-sm bg-amber-100 border border-amber-200" /> Partial
            </span>
            <span className="flex items-center gap-1">
              <span className="inline-block h-2 w-4 rounded-sm bg-blue-100 border border-blue-200" /> Assigned
            </span>
            <span className="flex items-center gap-1">
              <span className="inline-block h-2 w-4 rounded-sm bg-red-100 border border-red-200" /> Overbooked
            </span>
          </div>
        )}
      </button>

      {/* Grid body */}
      {!isCollapsed && (
        <div className="overflow-x-auto" data-testid="people-grid-body">
          <table className="w-full text-xs border-collapse">
            {firstBuckets.length > 0 && (
              <thead>
                <tr>
                  {/* Sticky name column header */}
                  <th className="sticky left-0 z-10 bg-card w-40 min-w-40 px-3 py-1 text-left text-muted-foreground font-normal border-b border-r">
                    Person
                  </th>
                  {firstBuckets.map((b, i) => (
                    <th
                      key={i}
                      className="px-1 py-1 text-center text-muted-foreground font-normal border-b min-w-10 w-10"
                    >
                      {bucketLabel(b, granularity)}
                    </th>
                  ))}
                </tr>
              </thead>
            )}
            <tbody>
              {people.map((person, pIdx) => {
                const q = utilQueries[pIdx];
                const buckets = q?.data?.buckets ?? [];
                const isLoadingRow = q?.isLoading;

                return (
                  <tr
                    key={person.id}
                    className="border-b last:border-b-0 hover:bg-accent/30"
                    data-testid={`person-row-${person.id}`}
                  >
                    {/* Sticky name cell */}
                    <td
                      className="sticky left-0 z-10 bg-card px-3 py-1.5 font-medium border-r truncate max-w-40 w-40 min-w-40"
                      title={person.name}
                    >
                      {person.name}
                    </td>

                    {/* Bucket cells */}
                    {isLoadingRow ? (
                      <td
                        colSpan={firstBuckets.length || 1}
                        className="px-2 py-1 text-muted-foreground italic"
                      >
                        Loading…
                      </td>
                    ) : buckets.length === 0 ? (
                      <td
                        colSpan={firstBuckets.length || 1}
                        className="px-2 py-1 text-muted-foreground"
                      >
                        No data
                      </td>
                    ) : (
                      buckets.map((bucket, bIdx) => {
                        const status = bucketStatus(bucket);
                        return (
                          <td
                            key={bIdx}
                            className={`border-r last:border-r-0 min-w-10 w-10 h-7 ${STATUS_CELL_CLASS[status]}`}
                            title={`${bucketLabel(bucket, granularity)}: ${Math.round(bucket.allocatedPercent)}% allocated`}
                            data-status={status}
                          />
                        );
                      })
                    )}
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
