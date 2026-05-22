import { useMemo, useState } from 'react';
import { useQueries, useQuery } from '@tanstack/react-query';
import { getResources, type ResourceInfo } from '@foundation/src/lib/api/resources-api';
import {
  getResourceUtilization,
  type ResourceUtilizationBucket,
} from '@foundation/src/lib/api/resource-utilization-api';
import { getPersonProfile, type PersonProfileInfo } from '@foundation/src/lib/api/person-profiles-api';
import type { OffTimeRange } from '@foundation/src/domain/scheduling/types';
import {
  startOfDay,
  startOfHour,
  startOfWeek,
  startOfMonth,
  addDays,
  addHours,
  addWeeks,
  addMonths,
  format,
} from 'date-fns';

// ── Types ────────────────────────────────────────────────────────────────────

type TimeScale = 'year' | 'month' | 'week' | 'day' | 'hour';

export interface PeopleUtilizationGridProps {
  anchorTs: Date;
  scale: TimeScale;
  /**
   * Site-level non-working ranges (availability events + weekends). Any
   * bucket overlapping one of these is rendered as “Off”, mirroring the
   * Spaces grid’s OffTimeOverlay behaviour. `resourceIds === null` means the
   * range applies to every resource (site-wide). When non-null, only those
   * person resources are affected.
   */
  offTimeRanges?: readonly OffTimeRange[];
}

interface ViewWindow {
  from: Date;
  to: Date;
  granularity: string;
}

// ── Helpers ──────────────────────────────────────────────────────────────────

function getViewWindow(anchorTs: Date, scale: TimeScale): ViewWindow {
  switch (scale) {
    case 'year': {
      // 12-month sliding window from the anchor's month — mirrors SchedulerGrid.
      // navigateTime('year', ±1) adds ±1 month, so each press shifts by one column.
      const ms = startOfMonth(anchorTs);
      return { from: ms, to: addMonths(ms, 12), granularity: 'week' };
    }
    case 'month': {
      // 5-week (35-day) sliding window from the anchor's Monday — mirrors SchedulerGrid.
      // navigateTime('month', ±1) adds ±1 week, so each press shifts by one week.
      const ws = startOfWeek(anchorTs, { weekStartsOn: 1 });
      return { from: ws, to: addDays(ws, 35), granularity: 'day' };
    }
    case 'week': {
      // 7-day sliding window from the anchor's day — mirrors SchedulerGrid.
      // navigateTime('week', ±1) adds ±1 day, so each press shifts by one column.
      const ds = startOfDay(anchorTs);
      return { from: ds, to: addDays(ds, 7), granularity: 'day' };
    }
    case 'day': {
      // 24-hour window from the anchor's hour — mirrors SchedulerGrid.
      // navigateTime('day', ±1) adds ±1 hour, so each press shifts by one column.
      const hs = startOfHour(anchorTs);
      return { from: hs, to: addHours(hs, 24), granularity: 'hour' };
    }
    default: {
      const ds = startOfDay(anchorTs);
      return { from: ds, to: addDays(ds, 7), granularity: 'day' };
    }
  }
}

// Generate column start-times from the view window — same math as the API uses
// for bucket boundaries. This lets headers render immediately on navigation
// without waiting for a data fetch.
function generateColumnDates(from: Date, to: Date, granularity: string): Date[] {
  const cols: Date[] = [];
  let cur = new Date(from);
  while (cur < to) {
    cols.push(new Date(cur));
    switch (granularity) {
      case 'hour':  cur = addHours(cur, 1); break;
      case 'day':   cur = addDays(cur, 1);  break;
      case 'week':  cur = addWeeks(cur, 1); break;
      default:      cur = addDays(cur, 1);  break;
    }
  }
  return cols;
}

function columnLabel(date: Date, granularity: string): string {
  switch (granularity) {
    case 'week': return format(date, 'MMM d');
    case 'day':  return format(date, 'EEE d');
    case 'hour': return format(date, 'HH:mm');
    default:     return format(date, 'MMM d');
  }
}

function bucketLabel(bucket: ResourceUtilizationBucket, granularity: string): string {
  return columnLabel(new Date(bucket.start), granularity);
}

import { type BucketStatus, STATUS_CELL_CLASS, STATUS_BORDER_CLASS } from './schedule-colors';

function bucketIsOff(
  bucket: ResourceUtilizationBucket,
  resourceId: string,
  offTimeRanges: readonly OffTimeRange[],
): boolean {
  if (offTimeRanges.length === 0) return false;
  const bucketStartMs = new Date(bucket.start).getTime();
  const bucketEndMs = new Date(bucket.end).getTime();
  return offTimeRanges.some((ot) => {
    if (ot.resourceIds !== null && !ot.resourceIds.includes(resourceId)) {
      return false;
    }
    // Standard half-open interval overlap.
    return ot.startMs < bucketEndMs && ot.endMs > bucketStartMs;
  });
}

function bucketStatus(
  bucket: ResourceUtilizationBucket,
  resourceId: string,
  offTimeRanges: readonly OffTimeRange[],
): BucketStatus {
  if (bucket.effectiveAvailabilityPercent === 0) return 'non-working';
  if (bucketIsOff(bucket, resourceId, offTimeRanges)) return 'non-working';
  if (bucket.isExclusiveOccupied)               return 'assigned';
  if (bucket.allocatedPercent === 0)             return 'available';
  if (bucket.allocatedPercent >= bucket.effectiveAvailabilityPercent) return 'overbooked';
  return 'partial';
}

function overallPercent(
  buckets: ResourceUtilizationBucket[],
  resourceId: string,
  offTimeRanges: readonly OffTimeRange[],
): number {
  if (!buckets.length) return 0;
  const working = buckets.filter(
    (b) =>
      b.effectiveAvailabilityPercent > 0 &&
      !bucketIsOff(b, resourceId, offTimeRanges),
  );
  if (!working.length) return 0;
  return Math.round(
    working.reduce((s, b) => s + b.allocatedPercent, 0) / working.length,
  );
}

function initials(name: string): string {
  return name
    .split(' ')
    .map((w) => w[0])
    .join('')
    .slice(0, 2)
    .toUpperCase();
}


// ── Legend dot ───────────────────────────────────────────────────────────────

function LegendDot({ status, label }: { status: BucketStatus; label: string }) {
  return (
    <span className="flex items-center gap-1">
      <span className={`inline-block h-2.5 w-4 rounded-sm border ${STATUS_CELL_CLASS[status]} ${STATUS_BORDER_CLASS[status]}`} />
      {label}
    </span>
  );
}

// ── Component ────────────────────────────────────────────────────────────────

export function PeopleUtilizationGrid({ anchorTs, scale, offTimeRanges = [] }: PeopleUtilizationGridProps) {
  const [search, setSearch] = useState('');

  const { from, to, granularity } = getViewWindow(anchorTs, scale);

  // Stabilise the off-time reference so downstream memos / overallPercent
  // don’t re-evaluate on every render when the parent rebuilds the array
  // identity without changing contents.
  const offTimes = useMemo(() => offTimeRanges, [offTimeRanges]);

  // 1. Fetch all active person resources
  const { data: peopleResponse, isLoading: peopleLoading } = useQuery({
    queryKey: ['resources', 'person', 'utilization-grid'],
    queryFn: () => getResources({ resourceTypeKey: 'person', isActive: true }),
  });
  const people: ResourceInfo[] = peopleResponse?.data ?? [];

  // Column dates are derived from the view window — no dependency on API data.
  // This means headers render immediately when the anchor changes, even before
  // the utilization queries for the new time range have completed.
  const columnDates = generateColumnDates(from, to, granularity);

  // 2. Fetch per-person utilization in parallel
  const utilQueries = useQueries({
    queries: people.map((p) => ({
      queryKey: ['resource-utilization', p.id, from.toISOString(), to.toISOString(), granularity],
      queryFn: () => getResourceUtilization(p.id, from, to, granularity),
      staleTime: 60_000,
      // Keep showing the previous period's data while the new fetch is in flight
      // so navigation feels fluid rather than flashing blank cells.
      placeholderData: (prev: typeof p | undefined) => prev,
    })),
  });

  // 3. Fetch per-person profiles for job title
  const profileQueries = useQueries({
    queries: people.map((p) => ({
      queryKey: ['person-profile', p.id],
      queryFn: () => getPersonProfile(p.id).catch(() => null),
      staleTime: 5 * 60_000,
    })),
  });

  const filteredPeople = people.filter((p) =>
    p.name.toLowerCase().includes(search.toLowerCase()),
  );

  if (peopleLoading) {
    return (
      <div className="h-full flex items-center justify-center text-sm text-muted-foreground">
        Loading people…
      </div>
    );
  }

  if (people.length === 0) {
    return (
      <div className="h-full flex items-center justify-center text-sm text-muted-foreground">
        No people defined yet.
      </div>
    );
  }

  return (
    <div className="h-full flex flex-col overflow-hidden bg-background" data-testid="people-utilization-grid">
      {/* Legend + Search strip */}
      <div className="flex items-center justify-between px-4 py-2 border-b bg-card shrink-0">
        <div className="flex items-center gap-4 text-xs text-muted-foreground">
          <LegendDot status="available"    label="Available" />
          <LegendDot status="partial"      label="Partial" />
          <LegendDot status="assigned"     label="Assigned" />
          <LegendDot status="overbooked"   label="Overbooked" />
          <LegendDot status="non-working"  label="Off" />
        </div>
        <input
          type="search"
          placeholder="Search people…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          className="h-8 w-48 rounded-md border bg-background px-3 text-sm outline-none focus:ring-1 focus:ring-ring"
        />
      </div>

      {/* Scrollable grid */}
      <div className="flex-1 overflow-auto" data-testid="people-grid-body">
        <table className="text-xs border-collapse" style={{ minWidth: '100%' }}>
          <thead className="sticky top-0 z-20 bg-card">
            <tr>
              {/* Sticky name column header */}
              <th className="sticky left-0 z-30 bg-card w-56 min-w-56 px-3 py-2 text-left text-muted-foreground font-normal border-b border-r">
                Person
              </th>
              {columnDates.map((date, i) => (
                <th
                  key={i}
                  className="px-1 py-2 text-center text-muted-foreground font-normal border-b border-r last:border-r-0 min-w-16 w-16"
                >
                  {columnLabel(date, granularity)}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {filteredPeople.length === 0 ? (
              <tr>
                <td colSpan={columnDates.length + 1} className="px-4 py-8 text-center text-muted-foreground">
                  No people match your search.
                </td>
              </tr>
            ) : (
              filteredPeople.map((person) => {
                const pIdx = people.indexOf(person);
                const q = utilQueries[pIdx];
                const buckets = q?.data?.buckets ?? [];
                const isLoadingRow = q?.isLoading;
                const profile = profileQueries[pIdx]?.data as PersonProfileInfo | null | undefined;
                const overallPct = overallPercent(buckets, person.id, offTimes);

                return (
                  <tr
                    key={person.id}
                    className="border-b last:border-b-0 hover:bg-accent/30"
                    data-testid={`person-row-${person.id}`}
                  >
                    {/* Sticky name cell */}
                    <td className="sticky left-0 z-10 bg-card px-3 py-2 border-r w-56 min-w-56">
                      <div className="flex items-center gap-2">
                        {/* Initials circle */}
                        <div className="h-7 w-7 rounded-full bg-primary/20 flex items-center justify-center text-xs font-semibold shrink-0 select-none">
                          {initials(person.name)}
                        </div>
                        <div className="min-w-0 flex-1">
                          <div className="font-medium truncate" title={person.name}>
                            {person.name}
                          </div>
                          {profile?.jobTitleName && (
                            <div className="text-muted-foreground truncate" title={profile.jobTitleName}>
                              {profile.jobTitleName}
                            </div>
                          )}
                        </div>
                        {/* Overall utilization % */}
                        <span
                          className={`font-semibold tabular-nums shrink-0 ${
                            overallPct > 100
                              ? 'text-red-500'
                              : overallPct > 0
                              ? 'text-foreground'
                              : 'text-muted-foreground'
                          }`}
                        >
                          {overallPct}%
                        </span>
                      </div>
                    </td>

                    {/* Bucket cells */}
                    {isLoadingRow ? (
                      <td
                        colSpan={columnDates.length || 1}
                        className="px-2 py-2 text-muted-foreground italic"
                      >
                        Loading…
                      </td>
                    ) : buckets.length === 0 ? (
                      <td
                        colSpan={columnDates.length || 1}
                        className="px-2 py-2 text-muted-foreground"
                      >
                        No data
                      </td>
                    ) : (
                      buckets.map((bucket, bIdx) => {
                        const status = bucketStatus(bucket, person.id, offTimes);
                        const pct = Math.round(bucket.allocatedPercent);
                        return (
                          <td
                            key={bIdx}
                            className={`border-r last:border-r-0 min-w-16 w-16 h-10 text-center font-medium align-middle ${STATUS_CELL_CLASS[status]}`}
                            title={`${bucketLabel(bucket, granularity)}: ${pct}% allocated`}
                            data-status={status}
                          >
                            {pct > 0 ? `${pct}%` : ''}
                          </td>
                        );
                      })
                    )}
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
