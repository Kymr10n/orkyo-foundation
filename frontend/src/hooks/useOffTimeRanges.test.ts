import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useOffTimeRanges } from './useOffTimeRanges';

const { mockUseSchedulingSettings, mockUseAvailabilityEvents } = vi.hoisted(() => ({
  mockUseSchedulingSettings: vi.fn((_?: unknown): { data: unknown } => ({ data: null })),
  mockUseAvailabilityEvents: vi.fn((_?: unknown): { data: unknown } => ({ data: [] })),
}));

vi.mock('@foundation/src/hooks/useScheduling', () => ({
  useSchedulingSettings: (arg?: unknown) => mockUseSchedulingSettings(arg),
  useAvailabilityEvents: (arg?: unknown) => mockUseAvailabilityEvents(arg),
}));

const ANCHOR = new Date('2026-12-10T00:00:00Z');

function shutdown(id: string, enabled: boolean, recurrenceRule?: string) {
  return {
    id,
    siteId: 'site-1',
    title: 'Shutdown',
    eventType: 'shutdown',
    defaultEffect: 'closed',
    startTs: '2026-12-24T00:00:00.000Z',
    endTs: '2026-12-26T00:00:00.000Z',
    isRecurring: recurrenceRule !== undefined,
    recurrenceRule,
    enabled,
  };
}

describe('useOffTimeRanges', () => {
  beforeEach(() => {
    mockUseSchedulingSettings.mockReturnValue({ data: null });
    mockUseAvailabilityEvents.mockReturnValue({ data: [] });
  });

  it('asks for the given site, and for none without one', () => {
    renderHook(() => useOffTimeRanges('site-1', ANCHOR));
    expect(mockUseAvailabilityEvents).toHaveBeenCalledWith('site-1');
    renderHook(() => useOffTimeRanges(null, ANCHOR));
    expect(mockUseSchedulingSettings).toHaveBeenLastCalledWith(undefined);
  });

  it('expands availability event recurrences in the site time zone', () => {
    // 2026-12-24T00:00Z is 19:00 on the 23rd in New York. Monthly in that zone, the March
    // occurrence keeps 19:00 local, which is 23:00Z once daylight saving time has started.
    mockUseAvailabilityEvents.mockReturnValue({ data: [shutdown('event-1', true, 'FREQ=MONTHLY;COUNT=4')] });
    mockUseSchedulingSettings.mockReturnValue({ data: { timeZone: 'America/New_York', weekendsEnabled: true } });

    const { result } = renderHook(() => useOffTimeRanges('site-1', ANCHOR));

    expect(result.current.map((r) => new Date(r.startMs).toISOString())).toEqual([
      '2026-12-24T00:00:00.000Z',
      '2027-01-24T00:00:00.000Z',
      '2027-02-24T00:00:00.000Z',
      '2027-03-23T23:00:00.000Z',
    ]);
  });

  it('generates weekend ranges when weekends are disabled', () => {
    mockUseSchedulingSettings.mockReturnValue({ data: { timeZone: 'UTC', weekendsEnabled: false } });
    const { result } = renderHook(() => useOffTimeRanges('site-1', ANCHOR));
    expect(result.current.length).toBeGreaterThan(0);
    expect(result.current.every((r) => r.title === 'Weekend')).toBe(true);
  });

  it('skips weekend ranges when weekends are enabled', () => {
    mockUseSchedulingSettings.mockReturnValue({ data: { timeZone: 'UTC', weekendsEnabled: true } });
    const { result } = renderHook(() => useOffTimeRanges('site-1', ANCHOR));
    expect(result.current).toEqual([]);
  });

  it('filters out disabled availability events', () => {
    mockUseAvailabilityEvents.mockReturnValue({ data: [shutdown('event-1', false), shutdown('event-2', true)] });
    mockUseSchedulingSettings.mockReturnValue({ data: { timeZone: 'UTC', weekendsEnabled: true } });

    const { result } = renderHook(() => useOffTimeRanges('site-1', ANCHOR));

    expect(result.current.map((r) => r.id)).toEqual(['event-2']);
  });

  it('keeps the same array while the anchor moves within its month', () => {
    const settings = { data: { timeZone: 'UTC', weekendsEnabled: true } };
    mockUseSchedulingSettings.mockReturnValue(settings);
    const events = { data: [shutdown('event-1', true)] };
    mockUseAvailabilityEvents.mockReturnValue(events);

    const { result, rerender } = renderHook(({ anchor }) => useOffTimeRanges('site-1', anchor), {
      initialProps: { anchor: ANCHOR },
    });
    const first = result.current;
    rerender({ anchor: new Date('2026-12-20T00:00:00Z') });
    expect(result.current).toBe(first);
    rerender({ anchor: new Date('2027-01-05T00:00:00Z') });
    expect(result.current).not.toBe(first);
  });
});
