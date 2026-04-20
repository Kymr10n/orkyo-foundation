import { describe, it, expect } from 'vitest';
import {
  buildIndex,
  getOverlapping,
  getStackIndex,
  getOverlapGroupSize,
  getMaxOverlapInSpace,
} from './schedule-index';
import type { PreviewEntry, PreviewSchedule } from './schedule-model';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const T = (iso: string) => new Date(iso).getTime();

function makeEntry(
  requestId: string,
  spaceId: string,
  startIso: string,
  endIso: string
): PreviewEntry {
  return {
    requestId,
    spaceId,
    startMs: T(startIso),
    endMs: T(endIso),
    name: requestId,
    minimalDurationMs: 3_600_000,
    isDraft: false,
  };
}

function makeSchedule(...entries: PreviewEntry[]): PreviewSchedule {
  const map: PreviewSchedule = new Map();
  for (const e of entries) map.set(e.requestId, e);
  return map;
}

// ---------------------------------------------------------------------------
// buildIndex
// ---------------------------------------------------------------------------

describe('buildIndex', () => {
  it('groups entries by spaceId', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T09:00Z');
    const b = makeEntry('b', 's2', '2024-01-01T08:00Z', '2024-01-01T09:00Z');
    const idx = buildIndex(makeSchedule(a, b));

    expect(idx.bySpace.get('s1')).toHaveLength(1);
    expect(idx.bySpace.get('s2')).toHaveLength(1);
  });

  it('sorts entries by startMs within each space', () => {
    const a = makeEntry('a', 's1', '2024-01-01T10:00Z', '2024-01-01T11:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T08:00Z', '2024-01-01T09:00Z');
    const idx = buildIndex(makeSchedule(a, b));
    const entries = idx.bySpace.get('s1')!;
    expect(entries[0].requestId).toBe('b');
    expect(entries[1].requestId).toBe('a');
  });

  it('breaks start-time ties by requestId (lexicographic)', () => {
    const a = makeEntry('z', 's1', '2024-01-01T08:00Z', '2024-01-01T09:00Z');
    const b = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T09:00Z');
    const idx = buildIndex(makeSchedule(a, b));
    const entries = idx.bySpace.get('s1')!;
    expect(entries[0].requestId).toBe('a');
    expect(entries[1].requestId).toBe('z');
  });

  it('returns empty bySpace for empty schedule', () => {
    const idx = buildIndex(new Map());
    expect(idx.bySpace.size).toBe(0);
  });
});

// ---------------------------------------------------------------------------
// getOverlapping — half-open intervals [start, end)
// ---------------------------------------------------------------------------

describe('getOverlapping', () => {
  it('detects a clear overlap', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T09:00Z', '2024-01-01T11:00Z');
    const idx = buildIndex(makeSchedule(a, b));
    expect(getOverlapping(idx, a)).toHaveLength(1);
    expect(getOverlapping(idx, a)[0].requestId).toBe('b');
  });

  it('does not include self', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const idx = buildIndex(makeSchedule(a));
    expect(getOverlapping(idx, a)).toHaveLength(0);
  });

  it('treats touching intervals as non-overlapping (exclusive end)', () => {
    // a ends at 10:00, b starts at 10:00 — they touch but do not overlap
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T10:00Z', '2024-01-01T12:00Z');
    const idx = buildIndex(makeSchedule(a, b));
    expect(getOverlapping(idx, a)).toHaveLength(0);
    expect(getOverlapping(idx, b)).toHaveLength(0);
  });

  it('does not return entries from a different space', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's2', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const idx = buildIndex(makeSchedule(a, b));
    expect(getOverlapping(idx, a)).toHaveLength(0);
  });

  it('detects fully contained overlap', () => {
    const outer = makeEntry('outer', 's1', '2024-01-01T08:00Z', '2024-01-01T12:00Z');
    const inner = makeEntry('inner', 's1', '2024-01-01T09:00Z', '2024-01-01T11:00Z');
    const idx = buildIndex(makeSchedule(outer, inner));
    expect(getOverlapping(idx, outer)).toHaveLength(1);
    expect(getOverlapping(idx, inner)).toHaveLength(1);
  });

  it('handles three-way overlap', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T11:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T09:00Z', '2024-01-01T12:00Z');
    const c = makeEntry('c', 's1', '2024-01-01T10:00Z', '2024-01-01T13:00Z');
    const idx = buildIndex(makeSchedule(a, b, c));
    expect(getOverlapping(idx, a)).toHaveLength(2);
    expect(getOverlapping(idx, b)).toHaveLength(2);
    expect(getOverlapping(idx, c)).toHaveLength(2);
  });
});

// ---------------------------------------------------------------------------
// getStackIndex
// ---------------------------------------------------------------------------

describe('getStackIndex', () => {
  it('returns 0 for the earliest of two overlapping entries', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T09:00Z', '2024-01-01T11:00Z');
    const idx = buildIndex(makeSchedule(a, b));
    expect(getStackIndex(idx, a)).toBe(0);
    expect(getStackIndex(idx, b)).toBe(1);
  });

  it('returns 0 for a non-overlapping entry', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T09:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T10:00Z', '2024-01-01T11:00Z');
    const idx = buildIndex(makeSchedule(a, b));
    expect(getStackIndex(idx, a)).toBe(0);
    expect(getStackIndex(idx, b)).toBe(0);
  });

  it('is deterministic on same-start ties (sorted by requestId)', () => {
    // 'a' < 'z' lexicographically → a gets index 0
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const z = makeEntry('z', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const idx = buildIndex(makeSchedule(a, z));
    expect(getStackIndex(idx, a)).toBe(0);
    expect(getStackIndex(idx, z)).toBe(1);
  });
});

// ---------------------------------------------------------------------------
// getOverlapGroupSize
// ---------------------------------------------------------------------------

describe('getOverlapGroupSize', () => {
  it('returns 1 when no overlaps', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T09:00Z');
    const idx = buildIndex(makeSchedule(a));
    expect(getOverlapGroupSize(idx, a)).toBe(1);
  });

  it('returns 2 for a two-way overlap', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T09:00Z', '2024-01-01T11:00Z');
    const idx = buildIndex(makeSchedule(a, b));
    expect(getOverlapGroupSize(idx, a)).toBe(2);
    expect(getOverlapGroupSize(idx, b)).toBe(2);
  });
});

// ---------------------------------------------------------------------------
// getMaxOverlapInSpace
// ---------------------------------------------------------------------------

describe('getMaxOverlapInSpace', () => {
  it('returns 1 for a single entry', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T09:00Z');
    const idx = buildIndex(makeSchedule(a));
    expect(getMaxOverlapInSpace(idx, 's1')).toBe(1);
  });

  it('returns 1 for a space with no entries', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T09:00Z');
    const idx = buildIndex(makeSchedule(a));
    expect(getMaxOverlapInSpace(idx, 's2')).toBe(1);
  });

  it('returns max simultaneous overlap across the space', () => {
    // a and b overlap, c does not overlap with either
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T09:00Z', '2024-01-01T11:00Z');
    const c = makeEntry('c', 's1', '2024-01-01T14:00Z', '2024-01-01T15:00Z');
    const idx = buildIndex(makeSchedule(a, b, c));
    expect(getMaxOverlapInSpace(idx, 's1')).toBe(2);
  });

  it('returns 3 when three entries all overlap', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T12:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T09:00Z', '2024-01-01T13:00Z');
    const c = makeEntry('c', 's1', '2024-01-01T10:00Z', '2024-01-01T14:00Z');
    const idx = buildIndex(makeSchedule(a, b, c));
    expect(getMaxOverlapInSpace(idx, 's1')).toBe(3);
  });
});
