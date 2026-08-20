import { describe, it, expect } from 'vitest';
import {
  EMPTY_RESOURCE_GRID_FILTER,
  UTILIZATION_FILTER_ORDER,
  filterResourceRows,
} from './resource-grid-filter';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';
import type { ResourceUtilizationSegment } from '@foundation/src/domain/scheduling/utilization-segments';
import type { BucketStatus } from './schedule-colors';

const resource = (id: string, name: string) => ({ id, name }) as ResourceInfo;

const ALICE = resource('a', 'Alice Smith');
const BOB = resource('b', 'Bob Jones');
const IDLE = resource('c', 'Carla Idle');
const ROWS = [ALICE, BOB, IDLE];

function segment(status: BucketStatus): ResourceUtilizationSegment {
  return {
    start: '2026-08-17T09:00:00Z',
    end: '2026-08-17T10:00:00Z',
    status,
    utilizationPercent: 50,
    sourceUnitCount: 1,
  };
}

// Alice is overbooked at some point; Bob is merely assigned; Carla has no segments at all.
const SEGMENTS = new Map<string, ResourceUtilizationSegment[]>([
  ['a', [segment('assigned'), segment('overbooked')]],
  ['b', [segment('assigned')]],
]);
const getSegments = (id: string) => SEGMENTS.get(id) ?? [];

const names = (rows: ResourceInfo[]) => rows.map((r) => r.name);

describe('filterResourceRows', () => {
  it('keeps every row when nothing is narrowed', () => {
    expect(filterResourceRows(ROWS, EMPTY_RESOURCE_GRID_FILTER, getSegments)).toHaveLength(3);
  });

  it('matches the resource name case-insensitively, anywhere in it', () => {
    const result = filterResourceRows(
      ROWS,
      { ...EMPTY_RESOURCE_GRID_FILTER, query: 'smith' },
      getSegments,
    );
    expect(names(result)).toEqual(['Alice Smith']);
  });

  it('ignores surrounding whitespace in the query', () => {
    const result = filterResourceRows(
      ROWS,
      { ...EMPTY_RESOURCE_GRID_FILTER, query: '  bob  ' },
      getSegments,
    );
    expect(names(result)).toEqual(['Bob Jones']);
  });

  it('keeps a row that is in the chosen state at any point in the period', () => {
    // "Who is overbooked this week" is the question the legend invites.
    const result = filterResourceRows(
      ROWS,
      { ...EMPTY_RESOURCE_GRID_FILTER, states: ['overbooked'] },
      getSegments,
    );
    expect(names(result)).toEqual(['Alice Smith']);
  });

  it('keeps every row that touches one of several chosen states', () => {
    const result = filterResourceRows(
      ROWS,
      { ...EMPTY_RESOURCE_GRID_FILTER, states: ['assigned'] },
      getSegments,
    );
    expect(names(result)).toEqual(['Alice Smith', 'Bob Jones']);
  });

  it('treats a row with no segments as available', () => {
    // An idle resource is exactly what a capacity question is looking for; dropping it would
    // hide the answer.
    const result = filterResourceRows(
      ROWS,
      { ...EMPTY_RESOURCE_GRID_FILTER, states: ['available'] },
      getSegments,
    );
    expect(names(result)).toEqual(['Carla Idle']);
  });

  it('combines the query and the states', () => {
    const result = filterResourceRows(
      ROWS,
      { query: 'o', states: ['overbooked'] },
      getSegments,
    );
    // Both Bob and Carla contain "o", but neither is overbooked.
    expect(names(result)).toEqual([]);
  });

  it('reads an empty state set as everything, never as nothing', () => {
    const result = filterResourceRows(ROWS, { query: '', states: [] }, getSegments);
    expect(result).toHaveLength(3);
  });

  it('defaults to every state the legend names', () => {
    expect(EMPTY_RESOURCE_GRID_FILTER.states).toEqual(UTILIZATION_FILTER_ORDER);
  });
});
