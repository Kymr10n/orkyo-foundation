import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';
import type { ResourceUtilizationSegment } from '@foundation/src/domain/scheduling/utilization-segments';
import type { BucketStatus } from './schedule-colors';

/**
 * The utilization states a reader can filter rows by — the same five the grid's legend names,
 * in the order it names them.
 */
export const UTILIZATION_FILTER_ORDER: BucketStatus[] = [
  'available',
  'partial',
  'assigned',
  'overbooked',
  'non-working',
];

export interface ResourceGridFilter {
  /** Matched against the resource name, case-insensitively. Empty matches everything. */
  query: string;
  /** A row survives when any of its segments is in one of these states. */
  states: readonly BucketStatus[];
}

export const EMPTY_RESOURCE_GRID_FILTER: ResourceGridFilter = {
  query: '',
  states: UTILIZATION_FILTER_ORDER,
};

/**
 * Narrows the rows of a resource utilization grid.
 *
 * A state filter asks "who was ever like this in the visible period", which is the question the
 * legend invites — "who is overbooked this week" is one click, not a scan. A row with no segments
 * at all is idle, so it answers to `available`: dropping it would hide exactly the resources a
 * capacity question is about.
 *
 * An empty state set means every state rather than none, for the same reason the schedule filters
 * do: a filter that can empty the grid is a way to conclude the data is gone.
 */
export function filterResourceRows(
  resources: readonly ResourceInfo[],
  filter: ResourceGridFilter,
  /** Reads a row's segments. An accessor rather than a map, so the caller reuses the one it has. */
  getSegments: (resourceId: string) => readonly ResourceUtilizationSegment[],
): ResourceInfo[] {
  const needle = filter.query.trim().toLowerCase();
  const everyState = filter.states.length === 0;

  return resources.filter((resource) => {
    if (needle && !resource.name.toLowerCase().includes(needle)) return false;
    if (everyState) return true;

    const segments = getSegments(resource.id);
    if (segments.length === 0) return filter.states.includes('available');

    return segments.some((segment) => filter.states.includes(segment.status));
  });
}
