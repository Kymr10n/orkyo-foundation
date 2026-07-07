import { useCallback, useDeferredValue, useEffect, useMemo, useState } from 'react';
import { useQueries, useQuery } from '@tanstack/react-query';
import { getResources, type ResourceInfo } from '@foundation/src/lib/api/resources-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import {
  getUtilizationByResource,
  type ResourceUtilizationBucket,
} from '@foundation/src/lib/api/resource-utilization-api';
import { getPersonJobTitles } from '@foundation/src/lib/api/person-profiles-api';
import {
  getResourceGroups,
  getResourceGroupMembers,
  type ResourceGroupInfo,
} from '@foundation/src/lib/api/resource-groups-api';
import {
  getAssignmentsByResourceType,
  validateAssignmentsBatch,
  type ResourceAssignmentInfo,
  type ValidateResourceAssignmentRequest,
} from '@foundation/src/lib/api/resource-assignments-api';
import type { OffTimeRange } from '@foundation/src/domain/scheduling/types';
import {
  mergeBucketsToSegments,
  type PersonUtilizationSegment,
} from '@foundation/src/domain/scheduling/utilization-segments';
import { LoadingSpinner } from '@foundation/src/components/ui/LoadingSpinner';
import { EmptyState } from '@foundation/src/components/ui/EmptyState';
import { Input } from '@foundation/src/components/ui/input';
import { PersonTimelineRow } from './PersonTimelineRow';
import { PersonAssignmentDialog } from './PersonAssignmentDialog';
import { TimelineGridShell, type ShellGroup } from './TimelineGridShell';
import { type BucketStatus, STATUS_CELL_CLASS, STATUS_BORDER_CLASS, STATUS_PATTERN_CLASS } from './schedule-colors';
import { groupRowsByResourceGroup } from './scheduler-types';
import type { TimeScale } from './ScaleSelect';
import {
  CONFLICT_CHECK_DELAY_MS,
  generateTimeColumns,
  overlapsOffTimeRange,
  utilizationGranularityForScale,
} from './time-grid-utils';
import { enrichColumnsWithOffTime } from './time-grid-offtime';

// ── Types ────────────────────────────────────────────────────────────────────

export interface PeopleUtilizationGridProps {
  anchorTs: Date;
  scale: TimeScale;
  /**
   * Site-level non-working ranges (availability events + weekends). Any
   * bucket overlapping one of these is rendered as “Off”, mirroring the
   * Spaces grid's off-time cell-tint behaviour. `resourceIds === null` means
   * the range applies to every resource (site-wide). When non-null, only
   * those person resources are affected.
   */
  offTimeRanges?: readonly OffTimeRange[];
  /** When true, weekend columns are highlighted to match the Spaces grid. */
  weekendsEnabled?: boolean;
  /**
   * Selected site. When set, rows are limited to people homed at the site or assigned there during
   * the visible window (server-filtered via the utilization query). Null = all people (no filter).
   */
  siteId?: string | null;
}

// ── Helpers ──────────────────────────────────────────────────────────────────

const EMPTY_SET: ReadonlySet<string> = new Set();

const EMPTY_UTILIZATION: { segments: PersonUtilizationSegment[]; overallPct: number } = {
  segments: [],
  overallPct: 0,
};

function bucketIsOff(
  bucket: ResourceUtilizationBucket,
  resourceId: string,
  offTimeRanges: readonly OffTimeRange[],
): boolean {
  return overlapsOffTimeRange(
    resourceId,
    new Date(bucket.start).getTime(),
    new Date(bucket.end).getTime(),
    offTimeRanges,
  );
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

// ── Legend dot ───────────────────────────────────────────────────────────────

function LegendDot({ status, label, title }: { status: BucketStatus; label: string; title?: string }) {
  return (
    <span className="flex items-center gap-1" title={title}>
      <span className={`inline-block h-2.5 w-4 rounded-sm border ${STATUS_CELL_CLASS[status]} ${STATUS_BORDER_CLASS[status]} ${STATUS_PATTERN_CLASS[status]}`} />
      {label}
    </span>
  );
}

// ── Component ────────────────────────────────────────────────────────────────

interface DialogState {
  personId: string;
  personName: string;
  allocationMode: string;
  start: string;
  end: string;
}

export function PeopleUtilizationGrid({ anchorTs, scale, offTimeRanges = [], weekendsEnabled, siteId }: PeopleUtilizationGridProps) {
  const [search, setSearch] = useState('');
  // Defer the filter so typing stays responsive — the input echoes `search`
  // immediately while the (heavier) row regrouping trails by a render.
  const deferredSearch = useDeferredValue(search);
  const [dialogState, setDialogState] = useState<DialogState | null>(null);

  const columns = useMemo(
    () => enrichColumnsWithOffTime(generateTimeColumns(scale, anchorTs, weekendsEnabled), offTimeRanges),
    [scale, anchorTs, weekendsEnabled, offTimeRanges],
  );
  const from = columns[0].start;
  const to = columns[columns.length - 1].end;
  const granularity = utilizationGranularityForScale(scale);
  const viewStartMs = from.getTime();
  const viewEndMs = to.getTime();

  const offTimes = useMemo(() => offTimeRanges, [offTimeRanges]);

  // 1. People resources — name/metadata lookup (tenant-wide). The visible row set is derived below
  //    from the utilization query, which is the site-filtered authority for "who's relevant".
  const { data: peopleResponse, isLoading: peopleLoading, isFetching: peopleFetching, isError: peopleError } = useQuery({
    queryKey: qk.resources.personUtilizationGrid(),
    queryFn: () => getResources({ resourceTypeKey: 'person', isActive: true }),
    staleTime: 60_000,
  });
  const allPeople: ResourceInfo[] = useMemo(() => peopleResponse?.data ?? [], [peopleResponse]);

  // 2. Person groups
  const { data: groupsData } = useQuery({
    queryKey: qk.resourceGroups.byType('person'),
    queryFn: () => getResourceGroups('person'),
  });
  const groups: ResourceGroupInfo[] = useMemo(() => groupsData ?? [], [groupsData]);

  // 3. Members per group — one query per group. Same pattern PersonList uses
  //    for per-person profile fetching. Acceptable at expected scale (tens).
  const memberQueries = useQueries({
    queries: groups.map((g) => ({
      queryKey: qk.resourceGroups.members(g.id),
      queryFn: () => getResourceGroupMembers(g.id),
      staleTime: 60_000,
    })),
  });

  // 4. Utilization for every person in a single request (replaces the old
  //    one-query-per-person fan-out). Grouped into a resourceId→buckets map.
  const { data: utilizationByResource = [], isLoading: utilizationLoading, isError: utilizationError, isPlaceholderData: utilizationIsPlaceholder } = useQuery({
    queryKey: qk.utilization.byResource('person', siteId ?? null, from, to, granularity),
    queryFn: () => getUtilizationByResource(from, to, granularity, 'person', siteId ?? undefined),
    staleTime: 60_000,
    placeholderData: (prev) => prev,
  });
  const bucketsByResource = useMemo(() => {
    const map = new Map<string, ResourceUtilizationBucket[]>();
    for (const r of utilizationByResource) map.set(r.resourceId, r.buckets);
    return map;
  }, [utilizationByResource]);

  // Visible rows: when a site is selected, the utilization query is site+window filtered server-side,
  // so its resource set is the authoritative row list (allPeople only supplies names/metadata).
  const people: ResourceInfo[] = useMemo(
    () => (siteId ? allPeople.filter((p) => bucketsByResource.has(p.id)) : allPeople),
    [siteId, allPeople, bucketsByResource],
  );

  // 5. Job-title labels in one request — replaces the old one-query-per-person fan-out (which also
  //    swallowed every failure with `.catch(() => null)`). Fetches only the label the grid renders,
  //    not the full profile; failures surface via `jobTitlesError` instead of disappearing silently.
  const peopleIds = useMemo(() => people.map((p) => p.id), [people]);
  const { data: jobTitles = [], isError: jobTitlesError } = useQuery({
    queryKey: qk.personJobTitles.byIds(peopleIds),
    queryFn: () => getPersonJobTitles(peopleIds),
    enabled: peopleIds.length > 0,
    staleTime: 5 * 60_000,
  });
  const jobTitleByResource = useMemo(() => {
    const map = new Map<string, string | undefined>();
    for (const j of jobTitles) map.set(j.resourceId, j.jobTitleName);
    return map;
  }, [jobTitles]);

  // 6. Assignments for every person in the window, in one request — drives the
  //    per-segment count badge. Grouped into a resourceId→assignments map.
  const { data: allAssignmentsFlat = [], isError: assignmentsError } = useQuery({
    queryKey: qk.utilization.assignmentsByType('person', from, to),
    queryFn: () => getAssignmentsByResourceType('person', from, to),
    staleTime: 60_000,
  });
  const assignmentsByResource = useMemo(() => {
    const map = new Map<string, ResourceAssignmentInfo[]>();
    for (const a of allAssignmentsFlat) {
      const list = map.get(a.resourceId) ?? [];
      list.push(a);
      map.set(a.resourceId, list);
    }
    return map;
  }, [allAssignmentsFlat]);

  // 7. Batch-validate all assignments to surface capability conflicts on bars.
  //    Deferred by CONFLICT_CHECK_DELAY_MS so the grid renders immediately and
  //    conflict badges appear in the background — this call is decorative only.
  const [conflictCheckReady, setConflictCheckReady] = useState(false);
  useEffect(() => {
    if (allAssignmentsFlat.length === 0) {
      setConflictCheckReady(false);
      return;
    }
    const id = setTimeout(() => setConflictCheckReady(true), CONFLICT_CHECK_DELAY_MS);
    return () => clearTimeout(id);
  }, [allAssignmentsFlat.length]);

  const { data: conflictedAssignmentIds = EMPTY_SET } = useQuery({
    queryKey: qk.utilization.capabilityConflicts(allAssignmentsFlat.map((a) => a.id)),
    queryFn: async (): Promise<Set<string>> => {
      const items: ValidateResourceAssignmentRequest[] = allAssignmentsFlat.map((a) => ({
        requestId: a.requestId,
        resourceId: a.resourceId,
        startUtc: a.startUtc,
        endUtc: a.endUtc,
        allocationPercent: a.allocationPercent,
        excludeAssignmentId: a.id,
      }));
      const results = await validateAssignmentsBatch(items);
      const conflicted = new Set<string>();
      for (const item of results) {
        if (item.result.blockers.some((b) => b.code === 'capability.missing')) {
          allAssignmentsFlat
            .filter((a) => a.requestId === item.requestId && a.resourceId === item.resourceId)
            .forEach((a) => conflicted.add(a.id));
        }
      }
      return conflicted;
    },
    enabled: conflictCheckReady,
    staleTime: 60_000,
  });

  // Membership is many-to-many (resolved via per-group member queries), so map
  // each person → the group ids they belong to. The grouping helper places a
  // person in the first matching group by displayOrder, preserving the original
  // first-wins dedup.
  const groupIdsByPerson = useMemo(() => {
    const map = new Map<string, string[]>();
    groups.forEach((g, idx) => {
      const members = memberQueries[idx]?.data?.members ?? [];
      for (const m of members) {
        const list = map.get(m.id);
        if (list) list.push(g.id);
        else map.set(m.id, [g.id]);
      }
    });
    return map;
  }, [groups, memberQueries]);

  // Build groups → people mapping. Groups sorted by displayOrder, empty groups
  // kept (includeEmpty: true) so users see their structure, ungrouped last.
  const shellGroups: ShellGroup<ResourceInfo>[] = useMemo(() => {
    const filtered = people.filter((p) =>
      p.name.toLowerCase().includes(deferredSearch.toLowerCase()),
    );
    return groupRowsByResourceGroup(
      filtered,
      groups,
      (p) => groupIdsByPerson.get(p.id) ?? [],
      { includeEmpty: true },
    );
  }, [people, groups, groupIdsByPerson, deferredSearch]);

  // Precompute each person's merged segments + overall percentage once per
  // bucket/off-time change, instead of re-deriving both inside every row's
  // render pass (renderRow runs for every visible row on any grid re-render).
  const utilizationByPerson = useMemo(() => {
    const map = new Map<string, { segments: PersonUtilizationSegment[]; overallPct: number }>();
    for (const [resourceId, buckets] of bucketsByResource) {
      map.set(resourceId, {
        segments: mergeBucketsToSegments(buckets, resourceId, offTimes),
        overallPct: overallPercent(buckets, resourceId, offTimes),
      });
    }
    return map;
  }, [bucketsByResource, offTimes]);

  const handleSegmentClick = useCallback(
    (p: ResourceInfo, seg: PersonUtilizationSegment) =>
      setDialogState({
        personId: p.id,
        personName: p.name,
        allocationMode: p.allocationMode,
        start: seg.start,
        end: seg.end,
      }),
    [],
  );

  // When a site is selected the visible row set is derived from the utilization buckets
  // (see `people` below), so rendering before they arrive shows a misleading "No people"
  // empty state. Gate the spinner on utilization in that case only — without a site the rows
  // come straight from `allPeople` and render progressively (per-row "Loading…"). `placeholderData:
  // prev` keeps utilizationLoading true only on first load, so window changes still update silently.
  if (
    peopleLoading ||
    (peopleFetching && allPeople.length === 0) ||
    (siteId && utilizationLoading) ||
    (siteId && utilizationIsPlaceholder && people.length === 0)
  ) {
    return <LoadingSpinner fullScreen={false} message="Loading people…" />;
  }

  // Surface load failures explicitly instead of swallowing them (a silent empty/stuck grid was
  // the confusing part). Covers the grid's data: people, utilization, assignments and profiles.
  if (peopleError || utilizationError || assignmentsError || jobTitlesError) {
    return (
      <div
        role="alert"
        className="h-full flex items-center justify-center text-sm text-destructive"
      >
        Couldn’t load people utilization. Please refresh to try again.
      </div>
    );
  }

  if (people.length === 0) {
    return (
      <EmptyState
        message="No people defined yet."
        className="h-full flex flex-col items-center justify-center text-sm"
      />
    );
  }

  const toolbar = (
    <div className="flex items-center justify-between px-4 py-2 border-b bg-card shrink-0">
      <div className="flex items-center gap-4 text-xs text-muted-foreground">
        <LegendDot status="available"    label="Available" />
        <LegendDot status="partial"      label="Booked" title="Booked % = share of this period the person is allocated (time-weighted)." />
        <LegendDot status="assigned"     label="Assigned" />
        <LegendDot status="overbooked"   label="Overbooked" title="Allocated beyond capacity (>100%) in this period." />
        <LegendDot status="non-working"  label="Off" />
      </div>
      <Input
        type="search"
        placeholder="Search people…"
        aria-label="Search people"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        className="h-8 w-48"
      />
    </div>
  );

  return (
    <>
      <TimelineGridShell<ResourceInfo>
        labelHeader="Person"
        columns={columns}
        scale={scale}
        groups={shellGroups}
        collapseIdPrefix="people"
        getRowId={(p) => p.id}
        emptyMessage="No people match your search."
        toolbar={toolbar}
        className="h-full flex flex-col overflow-hidden rounded-xl border bg-background"
        testId="people-utilization-grid"
        renderRow={(person) => {
          const { segments, overallPct } =
            utilizationByPerson.get(person.id) ?? EMPTY_UTILIZATION;
          const jobTitle = jobTitleByResource.get(person.id);
          const assignments = assignmentsByResource.get(person.id) ?? [];
          return (
            <PersonTimelineRow
              person={person}
              jobTitle={jobTitle}
              segments={segments}
              isLoadingRow={utilizationLoading}
              overallPct={overallPct}
              viewStartMs={viewStartMs}
              viewEndMs={viewEndMs}
              columns={columns}
              assignments={assignments}
              conflictedAssignmentIds={conflictedAssignmentIds}
              onSegmentClick={handleSegmentClick}
            />
          );
        }}
      />

      {dialogState && (
        <PersonAssignmentDialog
          open
          onOpenChange={(open) => { if (!open) setDialogState(null); }}
          personId={dialogState.personId}
          personName={dialogState.personName}
          allocationMode={dialogState.allocationMode}
          start={dialogState.start}
          end={dialogState.end}
        />
      )}
    </>
  );
}
