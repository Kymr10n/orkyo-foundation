import { describe, it, expect } from 'vitest';
import { makeRequest } from '@foundation/src/test-utils/request-fixtures';
import type { Request } from '@foundation/src/types/requests';
import {
  UNGROUPED_ID,
  UNGROUPED_NAME,
  UNKNOWN_PARENT_NAME,
  groupRequestsByParent,
  selectCanvasRequests,
} from './request-canvas-groups';

const VIEW_START = new Date('2026-03-02T00:00:00Z').getTime();
const VIEW_END = new Date('2026-03-09T00:00:00Z').getTime();

function scheduled(name: string, startTs: string, endTs: string, overrides: Partial<Request> = {}): Request {
  return makeRequest({ name, startTs, endTs, isScheduled: true, ...overrides });
}

describe('selectCanvasRequests', () => {
  it('drops requests without both dates', () => {
    const undated = makeRequest({ name: 'Backlog', startTs: null, endTs: null });
    const halfDated = makeRequest({ name: 'Half', startTs: '2026-03-03T08:00:00Z', endTs: null });
    expect(selectCanvasRequests([undated, halfDated], VIEW_START, VIEW_END)).toEqual([]);
  });

  it('drops requests entirely before or after the window, including ones touching its edges', () => {
    const before = scheduled('Before', '2026-02-27T08:00:00Z', '2026-03-01T08:00:00Z');
    const endsAtStart = scheduled('Ends at start', '2026-03-01T08:00:00Z', '2026-03-02T00:00:00Z');
    const startsAtEnd = scheduled('Starts at end', '2026-03-09T00:00:00Z', '2026-03-10T00:00:00Z');
    const after = scheduled('After', '2026-03-11T08:00:00Z', '2026-03-12T08:00:00Z');
    expect(selectCanvasRequests([before, endsAtStart, startsAtEnd, after], VIEW_START, VIEW_END)).toEqual([]);
  });

  it('keeps requests inside the window and ones straddling either edge', () => {
    const inside = scheduled('Inside', '2026-03-03T08:00:00Z', '2026-03-03T12:00:00Z');
    const straddlesStart = scheduled('Straddles start', '2026-03-01T08:00:00Z', '2026-03-02T08:00:00Z');
    const straddlesEnd = scheduled('Straddles end', '2026-03-08T20:00:00Z', '2026-03-09T08:00:00Z');
    const spansAll = scheduled('Spans all', '2026-02-01T00:00:00Z', '2026-04-01T00:00:00Z');
    expect(
      selectCanvasRequests([inside, straddlesStart, straddlesEnd, spansAll], VIEW_START, VIEW_END).map((r) => r.name),
    ).toEqual(['Inside', 'Straddles start', 'Straddles end', 'Spans all']);
  });
});

describe('groupRequestsByParent', () => {
  const parentA = makeRequest({ id: 'pa', name: 'Assembly', planningMode: 'summary', sortOrder: 2 });
  const parentB = makeRequest({ id: 'pb', name: 'Bodywork', planningMode: 'summary', sortOrder: 1 });
  const parentC = makeRequest({ id: 'pc', name: 'Casting', planningMode: 'summary', sortOrder: 1 });
  const known = new Map([parentA, parentB, parentC].map((p) => [p.id, p]));
  const resolve = (id: string) => known.get(id);

  it('returns no groups for no rows', () => {
    expect(groupRequestsByParent([], resolve)).toEqual([]);
  });

  it('puts parentless requests in a single trailing Ungrouped group', () => {
    const rows = [
      scheduled('Second', '2026-03-03T10:00:00Z', '2026-03-03T11:00:00Z'),
      scheduled('First', '2026-03-03T08:00:00Z', '2026-03-03T09:00:00Z'),
    ];
    const groups = groupRequestsByParent(rows, resolve);
    expect(groups).toHaveLength(1);
    expect(groups[0].id).toBe(UNGROUPED_ID);
    expect(groups[0].name).toBe(UNGROUPED_NAME);
    expect(groups[0].rows.map((r) => r.name)).toEqual(['First', 'Second']);
  });

  it('orders groups by the parent sortOrder, then name, and keys them by parent id', () => {
    const rows = [
      scheduled('a1', '2026-03-03T08:00:00Z', '2026-03-03T09:00:00Z', { parentRequestId: 'pa' }),
      scheduled('c1', '2026-03-03T08:00:00Z', '2026-03-03T09:00:00Z', { parentRequestId: 'pc' }),
      scheduled('b1', '2026-03-03T08:00:00Z', '2026-03-03T09:00:00Z', { parentRequestId: 'pb' }),
    ];
    const groups = groupRequestsByParent(rows, resolve);
    // Bodywork and Casting share sortOrder 1 and fall back to the name; Assembly (2) follows.
    expect(groups.map((g) => [g.id, g.name])).toEqual([
      ['pb', 'Bodywork'],
      ['pc', 'Casting'],
      ['pa', 'Assembly'],
    ]);
  });

  it('sorts the rows within a group by start, then sortOrder, then name', () => {
    const at8 = '2026-03-03T08:00:00Z';
    const rows = [
      scheduled('Zeta', at8, '2026-03-03T09:00:00Z', { parentRequestId: 'pa', sortOrder: 5 }),
      scheduled('Later', '2026-03-04T08:00:00Z', '2026-03-04T09:00:00Z', { parentRequestId: 'pa', sortOrder: 0 }),
      scheduled('Alpha', at8, '2026-03-03T09:00:00Z', { parentRequestId: 'pa', sortOrder: 5 }),
      scheduled('Ordered first', at8, '2026-03-03T09:00:00Z', { parentRequestId: 'pa', sortOrder: 1 }),
    ];
    const [group] = groupRequestsByParent(rows, resolve);
    expect(group.rows.map((r) => r.name)).toEqual(['Ordered first', 'Alpha', 'Zeta', 'Later']);
  });

  it('names an unresolvable parent "Unknown group" and places it after the known ones, before Ungrouped', () => {
    const rows = [
      scheduled('orphan', '2026-03-03T08:00:00Z', '2026-03-03T09:00:00Z', { parentRequestId: 'gone' }),
      scheduled('loose', '2026-03-03T08:00:00Z', '2026-03-03T09:00:00Z'),
      scheduled('a1', '2026-03-03T08:00:00Z', '2026-03-03T09:00:00Z', { parentRequestId: 'pa' }),
    ];
    const groups = groupRequestsByParent(rows, resolve);
    expect(groups.map((g) => g.name)).toEqual(['Assembly', UNKNOWN_PARENT_NAME, UNGROUPED_NAME]);
    // The unknown group still carries the parent's id, so a later resolution keeps its collapse state.
    expect(groups[1].id).toBe('gone');
  });

  it('orders two unknown parents by id so the list is stable across renders', () => {
    const rows = [
      scheduled('y', '2026-03-03T08:00:00Z', '2026-03-03T09:00:00Z', { parentRequestId: 'zz' }),
      scheduled('x', '2026-03-03T08:00:00Z', '2026-03-03T09:00:00Z', { parentRequestId: 'aa' }),
    ];
    expect(groupRequestsByParent(rows, () => undefined).map((g) => g.id)).toEqual(['aa', 'zz']);
  });

  it('omits Ungrouped when every row has a parent', () => {
    const rows = [scheduled('a1', '2026-03-03T08:00:00Z', '2026-03-03T09:00:00Z', { parentRequestId: 'pa' })];
    expect(groupRequestsByParent(rows, resolve).map((g) => g.id)).toEqual(['pa']);
  });
});
