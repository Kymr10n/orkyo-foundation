import { describe, it, expect } from 'vitest';
import { durationToMs, toScheduledEntry } from './schedule-model';
import type { Request } from '@foundation/src/types/requests';
import { spaceAssignment, makeAssignment } from '@foundation/src/test-utils/request-fixtures';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeRequest(overrides: Partial<Request> = {}): Request {
  return {
    id: 'req-1',
    name: 'Test request',
    status: 'planned',
    planningMode: 'leaf',
    sortOrder: 0,
    minimalDurationValue: 1,
    minimalDurationUnit: 'hours',
    schedulingSettingsApply: true,
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
    assignments: [],
    ...overrides,
  };
}

// ---------------------------------------------------------------------------
// durationToMs
// ---------------------------------------------------------------------------

describe('durationToMs', () => {
  it('converts minutes to ms', () => {
    expect(durationToMs(1, 'minutes')).toBe(60_000);
    expect(durationToMs(90, 'minutes')).toBe(5_400_000);
  });

  it('converts hours to ms', () => {
    expect(durationToMs(1, 'hours')).toBe(3_600_000);
    expect(durationToMs(2, 'hours')).toBe(7_200_000);
  });

  it('converts days to ms', () => {
    expect(durationToMs(1, 'days')).toBe(86_400_000);
  });

  it('converts weeks to ms', () => {
    expect(durationToMs(1, 'weeks')).toBe(604_800_000);
  });

  it('converts months to ms', () => {
    expect(durationToMs(1, 'months')).toBe(2_592_000_000);
  });

  it('converts years to ms', () => {
    expect(durationToMs(1, 'years')).toBe(31_536_000_000);
  });

  it('returns 0 for value 0', () => {
    expect(durationToMs(0, 'days')).toBe(0);
  });
});

// ---------------------------------------------------------------------------
// toScheduledEntry
// ---------------------------------------------------------------------------

describe('toScheduledEntry', () => {
  it('returns a ScheduledEntry for a fully scheduled request', () => {
    const req = makeRequest({
      assignments: [spaceAssignment('space-1')],
      startTs: '2024-06-01T08:00:00.000Z',
      endTs: '2024-06-01T10:00:00.000Z',
    });
    const entry = toScheduledEntry(req);
    expect(entry).not.toBeNull();
    expect(entry!.requestId).toBe('req-1');
    expect(entry!.resourceId).toBe('space-1');
    expect(entry!.startMs).toBe(new Date('2024-06-01T08:00:00.000Z').getTime());
    expect(entry!.endMs).toBe(new Date('2024-06-01T10:00:00.000Z').getTime());
    expect(entry!.minimalDurationMs).toBe(3_600_000); // 1 hour
  });

  it('returns null when resourceId is missing', () => {
    const req = makeRequest({ assignments: [], startTs: '2024-06-01T08:00:00Z', endTs: '2024-06-01T10:00:00Z' });
    expect(toScheduledEntry(req)).toBeNull();
  });

  it('returns null when startTs is missing', () => {
    const req = makeRequest({ assignments: [spaceAssignment('space-1')], endTs: '2024-06-01T10:00:00Z' });
    expect(toScheduledEntry(req)).toBeNull();
  });

  it('returns null when endTs is missing', () => {
    const req = makeRequest({ assignments: [spaceAssignment('space-1')], startTs: '2024-06-01T08:00:00Z' });
    expect(toScheduledEntry(req)).toBeNull();
  });

  it('returns null when startTs >= endTs (zero-duration)', () => {
    const req = makeRequest({
      assignments: [spaceAssignment('space-1')],
      startTs: '2024-06-01T10:00:00Z',
      endTs: '2024-06-01T10:00:00Z',
    });
    expect(toScheduledEntry(req)).toBeNull();
  });

  it('returns null when startTs > endTs (inverted)', () => {
    const req = makeRequest({
      assignments: [spaceAssignment('space-1')],
      startTs: '2024-06-01T12:00:00Z',
      endTs: '2024-06-01T08:00:00Z',
    });
    expect(toScheduledEntry(req)).toBeNull();
  });

  it('uses the space assignment when request also has person and tool assignments', () => {
    // Regression: Phase 6 exposed all assignment types; toScheduledEntry must
    // still pick only the space assignment for the grid entry.
    const req = makeRequest({
      assignments: [
        makeAssignment('person-1', 'person'),
        spaceAssignment('space-1'),
        makeAssignment('tool-1', 'tool'),
      ],
      startTs: '2024-06-01T08:00:00Z',
      endTs: '2024-06-01T10:00:00Z',
    });
    const entry = toScheduledEntry(req);
    expect(entry).not.toBeNull();
    expect(entry!.resourceId).toBe('space-1');
  });
});
