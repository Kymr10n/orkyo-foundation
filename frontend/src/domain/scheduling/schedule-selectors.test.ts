import { describe, it, expect } from 'vitest';
import {
  selectRequestDisplayData,
  selectSpaceOverlapCount,
  isOutsideView,
} from './schedule-selectors';
import { buildIndex } from './schedule-index';
import { evaluateSchedule } from './schedule-validator';
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
  minimalDurationMs = 3_600_000,
  isDraft = false
): PreviewEntry {
  return {
    requestId,
    spaceId,
    startMs: T(startIso),
    endMs: T(endIso),
    name: requestId,
    minimalDurationMs,
    isDraft,
  };
}

function makeSchedule(...entries: PreviewEntry[]): PreviewSchedule {
  const map: PreviewSchedule = new Map();
  for (const e of entries) map.set(e.requestId, e);
  return map;
}

// View window: 08:00 – 16:00 (8 hours)
const VIEW_START = T('2024-01-01T08:00Z');
const VIEW_END = T('2024-01-01T16:00Z');
const _VIEW_DURATION = VIEW_END - VIEW_START; // 8 h in ms

// ---------------------------------------------------------------------------
// isOutsideView
// ---------------------------------------------------------------------------

describe('isOutsideView', () => {
  it('returns false for an entry fully inside the view', () => {
    const e = makeEntry('a', 's1', '2024-01-01T09:00Z', '2024-01-01T11:00Z');
    expect(isOutsideView(e, VIEW_START, VIEW_END)).toBe(false);
  });

  it('returns true for an entry entirely before the view', () => {
    const e = makeEntry('a', 's1', '2024-01-01T05:00Z', '2024-01-01T07:00Z');
    expect(isOutsideView(e, VIEW_START, VIEW_END)).toBe(true);
  });

  it('returns true for an entry entirely after the view', () => {
    const e = makeEntry('a', 's1', '2024-01-01T17:00Z', '2024-01-01T18:00Z');
    expect(isOutsideView(e, VIEW_START, VIEW_END)).toBe(true);
  });

  it('returns false for an entry that starts before but ends inside the view', () => {
    const e = makeEntry('a', 's1', '2024-01-01T06:00Z', '2024-01-01T10:00Z');
    expect(isOutsideView(e, VIEW_START, VIEW_END)).toBe(false);
  });

  it('returns false for an entry that starts inside but ends after the view', () => {
    const e = makeEntry('a', 's1', '2024-01-01T14:00Z', '2024-01-01T18:00Z');
    expect(isOutsideView(e, VIEW_START, VIEW_END)).toBe(false);
  });

  it('returns false for an entry that spans the entire view', () => {
    const e = makeEntry('a', 's1', '2024-01-01T06:00Z', '2024-01-01T18:00Z');
    expect(isOutsideView(e, VIEW_START, VIEW_END)).toBe(false);
  });

  it('returns true when entry end == view start (touching, non-overlapping)', () => {
    const e = makeEntry('a', 's1', '2024-01-01T06:00Z', '2024-01-01T08:00Z');
    expect(isOutsideView(e, VIEW_START, VIEW_END)).toBe(true);
  });

  it('returns true when entry start == view end (touching, non-overlapping)', () => {
    const e = makeEntry('a', 's1', '2024-01-01T16:00Z', '2024-01-01T17:00Z');
    expect(isOutsideView(e, VIEW_START, VIEW_END)).toBe(true);
  });
});

// ---------------------------------------------------------------------------
// selectRequestDisplayData — geometry
// ---------------------------------------------------------------------------

describe('selectRequestDisplayData - geometry', () => {
  it('returns leftPercent = 0 for an entry starting at view start', () => {
    const e = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const schedule = makeSchedule(e);
    const idx = buildIndex(schedule);
    const validation = evaluateSchedule(schedule);
    const data = selectRequestDisplayData(e, idx, validation, VIEW_START, VIEW_END);
    expect(data.leftPercent).toBe(0);
  });

  it('returns correct widthPercent for a 2-hour slot in an 8-hour window', () => {
    const e = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const schedule = makeSchedule(e);
    const idx = buildIndex(schedule);
    const validation = evaluateSchedule(schedule);
    const data = selectRequestDisplayData(e, idx, validation, VIEW_START, VIEW_END);
    // 2h / 8h = 25%
    expect(data.widthPercent).toBeCloseTo(25, 5);
  });

  it('returns correct leftPercent for an entry starting 2 hours into the window', () => {
    const e = makeEntry('a', 's1', '2024-01-01T10:00Z', '2024-01-01T12:00Z');
    const schedule = makeSchedule(e);
    const idx = buildIndex(schedule);
    const validation = evaluateSchedule(schedule);
    const data = selectRequestDisplayData(e, idx, validation, VIEW_START, VIEW_END);
    // offset = 2h / 8h = 25%
    expect(data.leftPercent).toBeCloseTo(25, 5);
  });

  it('clamps widthPercent for an entry that extends beyond the view', () => {
    // Entry starts at 14:00 (6/8 = 75%) and ends at 18:00 (outside)
    const e = makeEntry('a', 's1', '2024-01-01T14:00Z', '2024-01-01T18:00Z');
    const schedule = makeSchedule(e);
    const idx = buildIndex(schedule);
    const validation = evaluateSchedule(schedule);
    const data = selectRequestDisplayData(e, idx, validation, VIEW_START, VIEW_END);
    // visible duration = 16:00 - 14:00 = 2h, view = 8h → 25%
    expect(data.widthPercent).toBeCloseTo(25, 5);
    expect(data.leftPercent).toBeCloseTo(75, 5);
  });
});

// ---------------------------------------------------------------------------
// selectRequestDisplayData — stacking
// ---------------------------------------------------------------------------

describe('selectRequestDisplayData - stacking', () => {
  it('topPx = 4 for a non-overlapping entry (stackIndex 0)', () => {
    const e = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const schedule = makeSchedule(e);
    const idx = buildIndex(schedule);
    const validation = evaluateSchedule(schedule);
    const data = selectRequestDisplayData(e, idx, validation, VIEW_START, VIEW_END);
    expect(data.topPx).toBe(4); // BASE_TOP_PAD_PX
  });

  it('topPx is greater for the later of two overlapping entries', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T09:00Z', '2024-01-01T11:00Z');
    const schedule = makeSchedule(a, b);
    const idx = buildIndex(schedule);
    const validation = evaluateSchedule(schedule);
    const dataA = selectRequestDisplayData(a, idx, validation, VIEW_START, VIEW_END);
    const dataB = selectRequestDisplayData(b, idx, validation, VIEW_START, VIEW_END);
    expect(dataA.topPx).toBeLessThan(dataB.topPx);
  });

  it('zIndex is higher for later-stacked entries', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T09:00Z', '2024-01-01T11:00Z');
    const schedule = makeSchedule(a, b);
    const idx = buildIndex(schedule);
    const validation = evaluateSchedule(schedule);
    const dataA = selectRequestDisplayData(a, idx, validation, VIEW_START, VIEW_END);
    const dataB = selectRequestDisplayData(b, idx, validation, VIEW_START, VIEW_END);
    expect(dataB.zIndex).toBeGreaterThan(dataA.zIndex);
  });
});

// ---------------------------------------------------------------------------
// selectRequestDisplayData — conflict / draft flags
// ---------------------------------------------------------------------------

describe('selectRequestDisplayData - conflict and draft flags', () => {
  it('hasConflict = false for a valid entry', () => {
    const e = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const schedule = makeSchedule(e);
    const idx = buildIndex(schedule);
    const validation = evaluateSchedule(schedule);
    const data = selectRequestDisplayData(e, idx, validation, VIEW_START, VIEW_END);
    expect(data.hasConflict).toBe(false);
  });

  it('hasConflict = true when entry has an overlap conflict', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T09:00Z', '2024-01-01T11:00Z');
    const schedule = makeSchedule(a, b);
    const idx = buildIndex(schedule);
    const validation = evaluateSchedule(schedule);
    const dataA = selectRequestDisplayData(a, idx, validation, VIEW_START, VIEW_END);
    expect(dataA.hasConflict).toBe(true);
  });

  it('isDraft = true when entry is a draft', () => {
    const e = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z', 3_600_000, true);
    const schedule = makeSchedule(e);
    const idx = buildIndex(schedule);
    const validation = evaluateSchedule(schedule);
    const data = selectRequestDisplayData(e, idx, validation, VIEW_START, VIEW_END);
    expect(data.isDraft).toBe(true);
  });

  it('isDraft = false for committed entry', () => {
    const e = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z', 3_600_000, false);
    const schedule = makeSchedule(e);
    const idx = buildIndex(schedule);
    const validation = evaluateSchedule(schedule);
    const data = selectRequestDisplayData(e, idx, validation, VIEW_START, VIEW_END);
    expect(data.isDraft).toBe(false);
  });
});

// ---------------------------------------------------------------------------
// selectSpaceOverlapCount
// ---------------------------------------------------------------------------

describe('selectSpaceOverlapCount', () => {
  it('returns 1 for a single entry', () => {
    const e = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T09:00Z');
    const idx = buildIndex(makeSchedule(e));
    expect(selectSpaceOverlapCount(idx, 's1')).toBe(1);
  });

  it('returns 2 for two overlapping entries', () => {
    const a = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T10:00Z');
    const b = makeEntry('b', 's1', '2024-01-01T09:00Z', '2024-01-01T11:00Z');
    const idx = buildIndex(makeSchedule(a, b));
    expect(selectSpaceOverlapCount(idx, 's1')).toBe(2);
  });

  it('returns 1 for a space with no entries', () => {
    const e = makeEntry('a', 's1', '2024-01-01T08:00Z', '2024-01-01T09:00Z');
    const idx = buildIndex(makeSchedule(e));
    expect(selectSpaceOverlapCount(idx, 's2')).toBe(1);
  });
});
