import { useCallback, useDeferredValue, useEffect, useMemo, useState } from 'react';
import { useQueries, useQuery } from '@tanstack/react-query';
import { getResources, type ResourceInfo } from '@foundation/src/lib/api/resources-api';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';
import { qk } from '@foundation/src/lib/api/query-keys';
import {
  getUtilizationByResource,
  type ResourceUtilizationBucket,
} from '@foundation/src/lib/api/resource-utilization-api';
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
import { useLookupFieldLabels } from '@foundation/src/hooks/useLookupFieldLabels';
import type { OffTimeRange } from '@foundation/src/domain/scheduling/types';
import {
  mergeBucketsToSegments,
  type ResourceUtilizationSegment,
} from '@foundation/src/domain/scheduling/utilization-segments';
import { LoadingSpinner } from '@foundation/src/components/ui/LoadingSpinner';
import { EmptyState } from '@foundation/src/components/ui/EmptyState';
import { ResourceTimelineRow } from './ResourceTimelineRow';
import { ResourceAssignmentDialog } from './ResourceAssignmentDialog';
import { TimelineGridShell, type ShellGroup } from './TimelineGridShell';
import {
  UTILIZATION_FILTER_ORDER,
  filterResourceRows,
  type ResourceGridFilter,
} from './resource-grid-filter';
import { groupRowsByResourceGroup } from './scheduler-types';
import type { TimeScale } from './ScaleSelect';
import {
  CONFLICT_CHECK_DELAY_MS,
  generateTimeColumns,
  overlapsOffTimeRange,
  utilizationGranularityForScale,
} from './time-grid-utils';
import { enrichColumnsWithOffTime } from './time-grid-offtime';

export interface ResourceUtilizationGridProps {
  /** The resource type whose rows this grid shows. Every query is scoped to its key. */
  resourceType: ResourceTypeInfo;
  anchorTs: Date;
  scale: TimeScale;
  /**
   * Site-level non-working ranges (availability events + weekends). Any bucket overlapping one of
   * these is rendered as "Off", mirroring the stations grid's off-time cell-tint behaviour.
   * `resourceIds === null` means the range applies to every resource (site-wide).
   */
  offTimeRanges?: readonly OffTimeRange[];
  weekendsEnabled?: boolean;
  siteId?: string | null;
  /**
   * Search and utilization-state filter. Owned by the Assets tab rather than by this grid: the tab
   * stacks one grid per selected type, and a search box per stack entry would ask the reader which
   * of three identical boxes to type in — and could only ever be labelled after one type.
   */
  filter: ResourceGridFilter;
}

const EMPTY_SET: ReadonlySet<string> = new Set();
const EMPTY_UTILIZATION: { segments: ResourceUtilizationSegment[]; overallPct: number } = {
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

// ── Component ────────────────────────────────────────────────────────────────

interface DialogState {
  resourceId: string;
  resourceName: string;
  allocationMode: string;
  start: string;
  end: string;
}

/** The lookup whose label names a row. Seeded on the person type by migration 1820. */
const JOB_TITLE_KEYS = ['job_title'] as const;

export function ResourceUtilizationGrid({ resourceType, anchorTs, scale, offTimeRanges = [], weekendsEnabled, siteId, filter }: ResourceUtilizationGridProps) {
  const typeKey = resourceType.key;
  // Everything here names the collection and uses the plural the type carries; the row-label
  // column heads "Name", because the tab and the rows already say what kind of thing these are.
  const typeNoun = resourceType.displayNamePlural.toLowerCase();
  // Defer the filter so typing stays responsive — the toolbar echoes the query immediately while
  // the (heavier) row regrouping trails by a render.
  const deferredFilter = useDeferredValue(filter);
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

  // 1. Resources of this type — name/metadata lookup (tenant-wide). The visible row set is derived below
  //    from the utilization query, which is the site-filtered authority for "who's relevant".
  const { data: resourcesResponse, isLoading: resourcesLoading, isFetching: resourcesFetching, isError: resourcesError } = useQuery({
    queryKey: qk.resources.utilizationGrid(typeKey),
    queryFn: () => getResources({ resourceTypeKey: typeKey, isActive: true }),
    staleTime: 60_000,
  });
  const allResources: ResourceInfo[] = useMemo(() => resourcesResponse?.data ?? [], [resourcesResponse]);

  // 2. Groups of this type
  const { data: groupsData } = useQuery({
    queryKey: qk.resourceGroups.byType(typeKey),
    queryFn: () => getResourceGroups(typeKey),
  });
  const groups: ResourceGroupInfo[] = useMemo(() => groupsData ?? [], [groupsData]);

  // 3. Members per group — one query per group. Same pattern PersonList uses
  //    for per-resource profile fetching. Acceptable at expected scale (tens).
  const memberQueries = useQueries({
    queries: groups.map((g) => ({
      queryKey: qk.resourceGroups.members(g.id),
      queryFn: () => getResourceGroupMembers(g.id),
      staleTime: 60_000,
    })),
  });

  // 4. Utilization for every resource in a single request (replaces the old
  //    one-query-per-resource fan-out). Grouped into a resourceId→buckets map.
  const { data: utilizationByResource = [], isLoading: utilizationLoading, isError: utilizationError, isPlaceholderData: utilizationIsPlaceholder } = useQuery({
    queryKey: qk.utilization.byResource(typeKey, siteId ?? null, from, to, granularity),
    queryFn: () => getUtilizationByResource(from, to, granularity, typeKey, siteId ?? undefined),
    staleTime: 60_000,
    placeholderData: (prev) => prev,
  });
  const bucketsByResource = useMemo(() => {
    const map = new Map<string, ResourceUtilizationBucket[]>();
    for (const r of utilizationByResource) map.set(r.resourceId, r.buckets);
    return map;
  }, [utilizationByResource]);

  // Visible rows: when a site is selected, the utilization query is site+window filtered server-side,
  // so its resource set is the authoritative row list (allResources only supplies names/metadata).
  const resources: ResourceInfo[] = useMemo(
    () => (siteId ? allResources.filter((p) => bucketsByResource.has(p.id)) : allResources),
    [siteId, allResources, bucketsByResource],
  );

  // 5. Job-title labels. A job title is a directory-profile concept, so only types declaring one
  //    have it; every other type falls back to its description below and issues no request.
  const showsJobTitles = resourceType.hasDirectoryProfile;
  // The job title is an organization list lookup since 1820, so it resolves from the resource's
  // own custom fields rather than from a person-shaped endpoint.
  const lookupLabels = useLookupFieldLabels(
    showsJobTitles ? resourceType.id : undefined,
    resources,
    JOB_TITLE_KEYS,
  );
  const jobTitleByResource = useMemo(() => {
    const map = new Map<string, string | undefined>();
    for (const r of resources) map.set(r.id, lookupLabels[r.id]?.job_title);
    return map;
  }, [resources, lookupLabels]);

  // 6. Assignments for every resource in the window, in one request — drives the
  //    per-segment count badge. Grouped into a resourceId→assignments map.
  const { data: allAssignmentsFlat = [], isError: assignmentsError } = useQuery({
    queryKey: qk.utilization.assignmentsByType(typeKey, from, to),
    queryFn: () => getAssignmentsByResourceType(typeKey, from, to),
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
  // Standing down when there is nothing to check is a render-phase update; arming the delay
  // is a real timer side effect and stays in the effect below.
  const hasAssignments = allAssignmentsFlat.length > 0;
  const [syncedHasAssignments, setSyncedHasAssignments] = useState(hasAssignments);
  if (syncedHasAssignments !== hasAssignments) {
    setSyncedHasAssignments(hasAssignments);
    if (!hasAssignments) setConflictCheckReady(false);
  }
  useEffect(() => {
    if (!hasAssignments) return;
    const id = setTimeout(() => setConflictCheckReady(true), CONFLICT_CHECK_DELAY_MS);
    return () => clearTimeout(id);
  }, [hasAssignments]);

  const { data: conflictedAssignmentIds = EMPTY_SET } = useQuery({
    // Keyed off the query that produced the assignments plus their count, not the id list
    // itself: React Query hashes the key on every render, and stringifying a thousand uuids
    // per keystroke into the search box is real main-thread time. The ids are a pure function
    // of that query's result, so this identifies the same set.
    queryKey: [
      ...qk.utilization.assignmentsByType(typeKey, from, to),
      'capability-conflicts',
      allAssignmentsFlat.length,
    ],
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
  // each resource → the group ids they belong to. The grouping helper places a
  // resource in the first matching group by displayOrder, preserving the original
  // first-wins dedup.
  const groupIdsByResource = useMemo(() => {
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

  // Precompute each resource's merged segments + overall percentage once per
  // bucket/off-time change, instead of re-deriving both inside every row's
  // render pass (renderRow runs for every visible row on any grid re-render).
  const utilizationByResourceId = useMemo(() => {
    const map = new Map<string, { segments: ResourceUtilizationSegment[]; overallPct: number }>();
    for (const [resourceId, buckets] of bucketsByResource) {
      map.set(resourceId, {
        segments: mergeBucketsToSegments(buckets, resourceId, offTimes),
        overallPct: overallPercent(buckets, resourceId, offTimes),
      });
    }
    return map;
  }, [bucketsByResource, offTimes]);


  // Build groups → resources mapping. Groups sorted by displayOrder, empty groups
  // kept (includeEmpty: true) so users see their structure, ungrouped last.
  const shellGroups: ShellGroup<ResourceInfo>[] = useMemo(() => {
    const filtered = filterResourceRows(
      resources,
      deferredFilter,
      (id) => utilizationByResourceId.get(id)?.segments ?? EMPTY_UTILIZATION.segments,
    );
    return groupRowsByResourceGroup(
      filtered,
      groups,
      (p) => groupIdsByResource.get(p.id) ?? [],
      { includeEmpty: true },
    );
  }, [resources, groups, groupIdsByResource, deferredFilter, utilizationByResourceId]);


  const handleSegmentClick = useCallback(
    (p: ResourceInfo, seg: ResourceUtilizationSegment) =>
      setDialogState({
        resourceId: p.id,
        resourceName: p.name,
        allocationMode: p.allocationMode,
        start: seg.start,
        end: seg.end,
      }),
    [],
  );

  // When a site is selected the visible row set is derived from the utilization buckets
  // (see `resources` below), so rendering before they arrive shows a misleading "No resources"
  // empty state. Gate the spinner on utilization in that case only — without a site the rows
  // come straight from `allResources` and render progressively (per-row "Loading…"). `placeholderData:
  // prev` keeps utilizationLoading true only on first load, so window changes still update silently.
  if (
    resourcesLoading ||
    (resourcesFetching && allResources.length === 0) ||
    (siteId && utilizationLoading) ||
    (siteId && utilizationIsPlaceholder && resources.length === 0)
  ) {
    return <LoadingSpinner fullScreen={false} message={`Loading ${typeNoun}…`} />;
  }

  // Surface load failures explicitly instead of swallowing them (a silent empty/stuck grid was
  // the confusing part). Covers the grid's data: resources, utilization, assignments and profiles.
  if (resourcesError || utilizationError || assignmentsError) {
    return (
      <div
        role="alert"
        className="h-full flex items-center justify-center text-sm text-destructive"
      >
        {`Couldn’t load ${typeNoun} utilization. Please refresh to try again.`}
      </div>
    );
  }

  // An empty grid has two causes with different fixes, and a bare "none" hides which. A
  // freshly defined type always starts here, so this is its author's first impression.
  if (resources.length === 0) {
    return (
      <EmptyState
        message={
          allResources.length === 0
            ? `No active ${typeNoun} yet. Add one to plan its utilization.`
            : `No ${typeNoun} at this site in this window. Clear the site filter, or give one a home site here.`
        }
        className="h-full flex flex-col items-center justify-center text-sm"
      />
    );
  }

  const isFiltered =
    filter.query.trim().length > 0 || filter.states.length !== UTILIZATION_FILTER_ORDER.length;
  const matchCount = shellGroups.reduce((total, group) => total + group.rows.length, 0);

  // Only the match count remains local: the tab owns one search and one filter for every stacked
  // type, but "3 of 12" is a fact about this grid and belongs over its own rows.
  const toolbar = isFiltered ? (
    <div className="px-4 py-1.5 border-b bg-card text-xs text-muted-foreground shrink-0">
      <span role="status">
        {matchCount} of {resources.length} {typeNoun}
      </span>
    </div>
  ) : undefined;

  return (
    <>
      <TimelineGridShell<ResourceInfo>
        labelHeader="Name"
        columns={columns}
        scale={scale}
        groups={shellGroups}
        collapseIdPrefix={typeKey}
        getRowId={(p) => p.id}
        emptyMessage={`No ${typeNoun} match your search.`}
        toolbar={toolbar}
        testId={`${typeKey}-utilization-grid`}
        renderRow={(resource) => {
          const { segments, overallPct } =
            utilizationByResourceId.get(resource.id) ?? EMPTY_UTILIZATION;
          const secondaryLabel = showsJobTitles
            ? jobTitleByResource.get(resource.id)
            : resource.description;
          const assignments = assignmentsByResource.get(resource.id) ?? [];
          return (
            <ResourceTimelineRow
              resource={resource}
              secondaryLabel={secondaryLabel}
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
        <ResourceAssignmentDialog
          open
          onOpenChange={(open) => { if (!open) setDialogState(null); }}
          resourceId={dialogState.resourceId}
          resourceName={dialogState.resourceName}
          allocationMode={dialogState.allocationMode}
          start={dialogState.start}
          end={dialogState.end}
        />
      )}
    </>
  );
}
