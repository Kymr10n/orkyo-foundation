import { describe, it, expect } from 'vitest';
import { evaluateSchedule, hasConflicts, getAllConflicts } from './schedule-validator';
import type { PreviewEntry, PreviewSchedule } from './schedule-model';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const T = (iso: string) => new Date(iso).getTime();

function makeEntry(
  requestId: string,
  spaceId: string,
  startIso: string,
  endIso: string,
  minimalDurationMs = 3_600_000
): PreviewEntry {
  return {
    requestId,
    spaceId,
    startMs: T(startIso),
    endMs: T(endIso),
    name: requestId,
    minimalDurationMs,
    isDraft: false,
  };
}

function makeSchedule(...entries: PreviewEntry[]): PreviewSchedule {
  const map: PreviewSchedule = new Map();
  for (const e of entries) map.set(e.requestId, e);
  return map;
}

// ---------------------------------------------------------------------------
// evaluateSchedule
// ---------------------------------------------------------------------------

describe('evaluateSchedule', () => {
  it('returns empty result for an empty schedule', () => {
    const result = evaluateSchedule(new Map());
    expect(result.size).toBe(0);
  });

  it('returns no conflicts for a single valid entry', () => {
    const entry = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z', 3_600_000);
    const result = evaluateSchedule(makeSchedule(entry));
    expect(result.size).toBe(0);
  });

  // --- below_min_duration ---

  it('flags below_min_duration when actual duration < minimum', () => {
    // 30-minute slot, 1-hour minimum
    const entry = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T08:30Z', 3_600_000);
    const result = evaluateSchedule(makeSchedule(entry));
    expect(result.has('a')).toBe(true);
    const conflicts = result.get('a')!;
    expect(conflicts.some((c) => c.kind === 'below_min_duration')).toBe(true);
    expect(conflicts.every((c) => c.severity === 'error')).toBe(true);
  });

  it('does not flag below_min_duration when duration equals minimum exactly', () => {
    // Exactly 1 hour — boundary case
    const entry = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T09:00Z', 3_600_000);
    const result = evaluateSchedule(makeSchedule(entry));
    expect(result.has('a')).toBe(false);
  });

  it('does not flag below_min_duration when duration exceeds minimum', () => {
    const entry = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T11:00Z', 3_600_000);
    const result = evaluateSchedule(makeSchedule(entry));
    expect(result.has('a')).toBe(false);
  });

  // --- overlap ---

  it('flags overlap for two requests in the same space with overlapping times', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T09:00Z', '2024-01-01T11:00Z');
    const result = evaluateSchedule(makeSchedule(a, b));
    expect(result.has('a')).toBe(true);
    expect(result.has('b')).toBe(true);
    expect(result.get('a')!.some((c) => c.kind === 'overlap')).toBe(true);
    expect(result.get('b')!.some((c) => c.kind === 'overlap')).toBe(true);
  });

  it('does not flag overlap for touching intervals (exclusive end)', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T10:00Z', '2024-01-01T12:00Z');
    const result = evaluateSchedule(makeSchedule(a, b));
    expect(result.has('a')).toBe(false);
    expect(result.has('b')).toBe(false);
  });

  it('does not flag overlap for requests in different spaces', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's2', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const result = evaluateSchedule(makeSchedule(a, b));
    expect(result.has('a')).toBe(false);
    expect(result.has('b')).toBe(false);
  });

  it('produces one overlap conflict per overlapping peer', () => {
    // a overlaps with both b and c
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T12:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T09:00Z', '2024-01-01T13:00Z');
    const c = makeEntry('c', 's1', '2024-01-01T10:00Z', '2024-01-01T14:00Z');
    const result = evaluateSchedule(makeSchedule(a, b, c));
    const aConflicts = result.get('a')!.filter((x) => x.kind === 'overlap');
    expect(aConflicts).toHaveLength(2);
  });

  // --- capacity ---

  it('allows overlaps when within space capacity', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T09:00Z', '2024-01-01T11:00Z');
    const capacities = new Map([['s1', 2]]);
    const result = evaluateSchedule(makeSchedule(a, b), capacities);
    // 2 concurrent, capacity 2 → no conflict
    expect(result.has('a')).toBe(false);
    expect(result.has('b')).toBe(false);
  });

  it('flags capacity_exceeded when concurrent allocations exceed capacity', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const c = makeEntry('c', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const capacities = new Map([['s1', 2]]);
    const result = evaluateSchedule(makeSchedule(a, b, c), capacities);
    // 3 concurrent, capacity 2 → all three should be flagged
    for (const id of ['a', 'b', 'c']) {
      expect(result.has(id)).toBe(true);
      expect(result.get(id)!.some((c) => c.kind === 'capacity_exceeded')).toBe(true);
    }
  });

  it('capacity_exceeded message includes concurrent count', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const c = makeEntry('c', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const capacities = new Map([['s1', 2]]);
    const result = evaluateSchedule(makeSchedule(a, b, c), capacities);
    const conflict = result.get('a')!.find((c) => c.kind === 'capacity_exceeded')!;
    expect(conflict.message).toContain('3/2');
  });

  it('uses default capacity of 1 when space is not in capacity map', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T09:00Z', '2024-01-01T11:00Z');
    const capacities = new Map([['s2', 5]]); // s1 not in map
    const result = evaluateSchedule(makeSchedule(a, b), capacities);
    // Falls back to capacity=1, so classic overlap
    expect(result.get('a')!.some((c) => c.kind === 'overlap')).toBe(true);
  });

  it('does not flag capacity_exceeded at exact capacity boundary', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const c = makeEntry('c', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const capacities = new Map([['s1', 3]]);
    const result = evaluateSchedule(makeSchedule(a, b, c), capacities);
    // 3 concurrent, capacity 3 → exactly at limit, no conflict
    expect(result.has('a')).toBe(false);
    expect(result.has('b')).toBe(false);
    expect(result.has('c')).toBe(false);
  });

  it('handles mixed spaces with different capacities', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const c = makeEntry('c', 's2', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const d = makeEntry('d', 's2', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const capacities = new Map([['s1', 2], ['s2', 1]]);
    const result = evaluateSchedule(makeSchedule(a, b, c, d), capacities);
    // s1: 2 concurrent, capacity 2 → ok
    expect(result.has('a')).toBe(false);
    expect(result.has('b')).toBe(false);
    // s2: 2 concurrent, capacity 1 → overlap
    expect(result.get('c')!.some((c) => c.kind === 'overlap')).toBe(true);
    expect(result.get('d')!.some((c) => c.kind === 'overlap')).toBe(true);
  });

  // --- combined ---

  it('reports both overlap and below_min_duration on the same entry', () => {
    // Very short slot that also overlaps
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T08:10Z', 3_600_000);
    const b = makeEntry('b', 's1', '2024-01-01T08:05Z', '2024-01-01T09:00Z');
    const result = evaluateSchedule(makeSchedule(a, b));
    const aConflicts = result.get('a')!;
    expect(aConflicts.some((c) => c.kind === 'below_min_duration')).toBe(true);
    expect(aConflicts.some((c) => c.kind === 'overlap')).toBe(true);
  });

  it('conflict ids are unique per entry', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T09:00Z', '2024-01-01T11:00Z');
    const c = makeEntry('c', 's1', '2024-01-01T09:30Z', '2024-01-01T11:30Z');
    const result = evaluateSchedule(makeSchedule(a, b, c));
    for (const conflicts of result.values()) {
      const ids = conflicts.map((x) => x.id);
      expect(new Set(ids).size).toBe(ids.length);
    }
  });
});

// ---------------------------------------------------------------------------
// hasConflicts
// ---------------------------------------------------------------------------

describe('hasConflicts', () => {
  it('returns true when the request has conflicts', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T09:00Z', '2024-01-01T11:00Z');
    const result = evaluateSchedule(makeSchedule(a, b));
    expect(hasConflicts(result, 'a')).toBe(true);
  });

  it('returns false when the request has no conflicts', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T09:00Z');
    const result = evaluateSchedule(makeSchedule(a));
    expect(hasConflicts(result, 'a')).toBe(false);
  });

  it('returns false for an unknown requestId', () => {
    const result = evaluateSchedule(new Map());
    expect(hasConflicts(result, 'nonexistent')).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// getAllConflicts
// ---------------------------------------------------------------------------

describe('getAllConflicts', () => {
  it('returns empty array for no conflicts', () => {
    expect(getAllConflicts(new Map())).toHaveLength(0);
  });

  it('flattens conflicts from all requests', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T09:00Z', '2024-01-01T11:00Z');
    const result = evaluateSchedule(makeSchedule(a, b));
    // Each of a and b gets one overlap conflict against the other
    expect(getAllConflicts(result).length).toBeGreaterThanOrEqual(2);
  });
});
