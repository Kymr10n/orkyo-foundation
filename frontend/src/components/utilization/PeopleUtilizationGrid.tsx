import { useMemo, useState } from 'react';
import { useQueries, useQuery } from '@tanstack/react-query';
import { useShallow } from 'zustand/react/shallow';
import { getResources, type ResourceInfo } from '@foundation/src/lib/api/resources-api';
import {
  getResourceUtilization,
  type ResourceUtilizationBucket,
} from '@foundation/src/lib/api/resource-utilization-api';
import { getPersonProfile, type PersonProfileInfo } from '@foundation/src/lib/api/person-profiles-api';
import {
  getResourceGroups,
  getResourceGroupMembers,
  type ResourceGroupInfo,
} from '@foundation/src/lib/api/resource-groups-api';
import {
  getAssignmentsByResource,
  type ResourceAssignmentInfo,
} from '@foundation/src/lib/api/resource-assignments-api';
import { useAppStore } from '@foundation/src/store/app-store';
import type { OffTimeRange } from '@foundation/src/domain/scheduling/types';
import { mergeBucketsToSegments } from '@foundation/src/domain/scheduling/utilization-segments';
import { GroupHeader } from './GroupHeader';
import { PersonTimelineRow } from './PersonTimelineRow';
import { PersonAssignmentDialog } from './PersonAssignmentDialog';
import { type BucketStatus, STATUS_CELL_CLASS, STATUS_BORDER_CLASS } from './schedule-colors';
import type { PeopleByGroup } from './scheduler-types';
import type { TimeScale } from './ScaleSelect';
import {
  generateTimeColumns,
  overlapsOffTimeRange,
  utilizationGranularityForScale,
} from './time-grid-utils';

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
}

// ── Helpers ──────────────────────────────────────────────────────────────────

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

function LegendDot({ status, label }: { status: BucketStatus; label: string }) {
  return (
    <span className="flex items-center gap-1">
      <span className={`inline-block h-2.5 w-4 rounded-sm border ${STATUS_CELL_CLASS[status]} ${STATUS_BORDER_CLASS[status]}`} />
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

export function PeopleUtilizationGrid({ anchorTs, scale, offTimeRanges = [] }: PeopleUtilizationGridProps) {
  const [search, setSearch] = useState('');
  const [dialogState, setDialogState] = useState<DialogState | null>(null);

  const columns = generateTimeColumns(scale, anchorTs);
  const from = columns[0].start;
  const to = columns[columns.length - 1].end;
  const granularity = utilizationGranularityForScale(scale);
  const viewStartMs = from.getTime();
  const viewEndMs = to.getTime();

  // Shared with the Spaces grid — same Zustand slice, session-scoped.
  const { collapsedGroupIds, toggleGroupCollapse } = useAppStore(
    useShallow((s) => ({
      collapsedGroupIds: s.collapsedGroupIds,
      toggleGroupCollapse: s.toggleGroupCollapse,
    })),
  );

  const offTimes = useMemo(() => offTimeRanges, [offTimeRanges]);

  // 1. People resources
  const { data: peopleResponse, isLoading: peopleLoading } = useQuery({
    queryKey: ['resources', 'person', 'utilization-grid'],
    queryFn: () => getResources({ resourceTypeKey: 'person', isActive: true }),
  });
  const people: ResourceInfo[] = useMemo(() => peopleResponse?.data ?? [], [peopleResponse]);

  // 2. Person groups
  const { data: groupsData } = useQuery({
    queryKey: ['resource-groups', 'person'],
    queryFn: () => getResourceGroups('person'),
  });
  const groups: ResourceGroupInfo[] = useMemo(() => groupsData ?? [], [groupsData]);

  // 3. Members per group — one query per group. Same pattern PersonList uses
  //    for per-person profile fetching. Acceptable at expected scale (tens).
  const memberQueries = useQueries({
    queries: groups.map((g) => ({
      queryKey: ['resource-group-members', g.id],
      queryFn: () => getResourceGroupMembers(g.id),
      staleTime: 60_000,
    })),
  });

  // 4. Per-person utilization
  const utilQueries = useQueries({
    queries: people.map((p) => ({
      queryKey: ['resource-utilization', p.id, from.toISOString(), to.toISOString(), granularity],
      queryFn: () => getResourceUtilization(p.id, from, to, granularity),
      staleTime: 60_000,
      placeholderData: (prev: typeof p | undefined) => prev,
    })),
  });

  // 5. Per-person profile (for job title in the label cell)
  const profileQueries = useQueries({
    queries: people.map((p) => ({
      queryKey: ['person-profile', p.id],
      queryFn: () => getPersonProfile(p.id).catch(() => null),
      staleTime: 5 * 60_000,
    })),
  });

  // 6. Per-person assignments in the view window — drives the per-segment count badge.
  const assignmentQueries = useQueries({
    queries: people.map((p) => ({
      queryKey: ['resource-assignments', p.id, from.toISOString(), to.toISOString()],
      queryFn: () => getAssignmentsByResource(p.id, from, to).catch(() => [] as ResourceAssignmentInfo[]),
      staleTime: 60_000,
    })),
  });

  // Build a personId → utilization-query-index lookup once.
  const personIndex = useMemo(() => {
    const map = new Map<string, number>();
    people.forEach((p, i) => map.set(p.id, i));
    return map;
  }, [people]);

  // Build groups → people mapping. Mirrors SchedulerGrid's sort logic: groups
  // sorted by displayOrder, ungrouped bucket last.
  const peopleByGroup: PeopleByGroup[] = useMemo(() => {
    if (people.length === 0) return [];

    // Map groupId → resource IDs that belong to it
    const groupIdToMemberIds = new Map<string, Set<string>>();
    groups.forEach((g, idx) => {
      const members = memberQueries[idx]?.data?.members ?? [];
      groupIdToMemberIds.set(g.id, new Set(members.map((m) => m.id)));
    });

    // Track which people are unassigned (in no group)
    const assignedPersonIds = new Set<string>();
    groupIdToMemberIds.forEach((set) => set.forEach((id) => assignedPersonIds.add(id)));

    const peopleById = new Map(people.map((p) => [p.id, p]));

    const sortedGroups = [...groups].sort(
      (a, b) => (a.displayOrder ?? 0) - (b.displayOrder ?? 0),
    );

    const filterFn = (p: ResourceInfo) =>
      p.name.toLowerCase().includes(search.toLowerCase());

    const result: PeopleByGroup[] = [];
    for (const g of sortedGroups) {
      const ids = groupIdToMemberIds.get(g.id) ?? new Set();
      const members = Array.from(ids)
        .map((id) => peopleById.get(id))
        .filter((p): p is ResourceInfo => Boolean(p))
        .filter(filterFn);
      // Show empty groups too, so users see their structure — match Spaces grid.
      result.push({
        groupId: g.id,
        groupName: g.name,
        groupColor: g.color,
        people: members,
      });
    }

    const ungrouped = people
      .filter((p) => !assignedPersonIds.has(p.id))
      .filter(filterFn);
    if (ungrouped.length > 0) {
      result.push({
        groupId: 'ungrouped',
        groupName: 'Ungrouped',
        people: ungrouped,
      });
    }
    return result;
  }, [people, groups, memberQueries, search]);

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

  const totalVisible = peopleByGroup.reduce((s, g) => s + g.people.length, 0);

  return (
    <div className="h-full flex flex-col overflow-hidden rounded-xl border bg-background m-3" data-testid="people-utilization-grid">
      {/* Legend + Search strip */}
      <div className="flex items-center justify-between px-4 py-2 border-b bg-card shrink-0">
        <div className="flex items-center gap-4 text-xs text-muted-foreground">
          <LegendDot status="available"    label="Available" />
          <LegendDot status="partial"      label="Booked" />
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

      {/* Header row — same shape as SchedulerGrid */}
      <div className="flex border-b bg-muted/50 overflow-hidden">
        <div className="w-52 flex-shrink-0 px-3 py-2 border-r text-xs font-medium text-muted-foreground">
          Person
        </div>
        <div className="flex-1 flex">
          {columns.map((column, i) => (
            <div
              key={i}
              className="flex-1 min-w-[60px] px-3 py-2 border-r text-center text-xs font-medium text-muted-foreground"
            >
              {column.label}
            </div>
          ))}
        </div>
      </div>

      {/* Scrollable body */}
      <div className="flex-1 overflow-auto" data-testid="people-grid-body">
        {totalVisible === 0 ? (
          <div className="px-4 py-8 text-center text-sm text-muted-foreground">
            No people match your search.
          </div>
        ) : (
          peopleByGroup.map((group) => {
            const collapseId = `people:${group.groupId}`;
            const isCollapsed = collapsedGroupIds.includes(collapseId);
            return (
              <div key={group.groupId}>
                <GroupHeader
                  groupName={group.groupName}
                  groupColor={group.groupColor}
                  count={group.people.length}
                  isCollapsed={isCollapsed}
                  onToggle={() => toggleGroupCollapse(collapseId)}
                />
                {!isCollapsed &&
                  group.people.map((person) => {
                    const pIdx = personIndex.get(person.id) ?? -1;
                    const q = pIdx >= 0 ? utilQueries[pIdx] : undefined;
                    const buckets = q?.data?.buckets ?? [];
                    const isLoadingRow = !!q?.isLoading;
                    const profile = (pIdx >= 0
                      ? (profileQueries[pIdx]?.data as PersonProfileInfo | null | undefined)
                      : null);
                    const overallPct = overallPercent(buckets, person.id, offTimes);

                    const assignments = (pIdx >= 0 ? (assignmentQueries[pIdx]?.data ?? []) : []) as ResourceAssignmentInfo[];
                    const segments = mergeBucketsToSegments(buckets, person.id, offTimes);
                    return (
                      <PersonTimelineRow
                        key={`${group.groupId}-${person.id}`}
                        person={person}
                        jobTitle={profile?.jobTitleName}
                        segments={segments}
                        isLoadingRow={isLoadingRow}
                        overallPct={overallPct}
                        viewStartMs={viewStartMs}
                        viewEndMs={viewEndMs}
                        assignments={assignments}
                        onSegmentClick={(p, seg) =>
                          setDialogState({
                            personId: p.id,
                            personName: p.name,
                            allocationMode: p.allocationMode,
                            start: seg.start,
                            end: seg.end,
                          })
                        }
                      />
                    );
                  })}
              </div>
            );
          })
        )}
      </div>

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
    </div>
  );
}
