import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';
import { useOffTimeRanges } from './useOffTimeRanges';
import { expandRecurrence } from '@foundation/src/domain/scheduling/recurrence';
import { generateWeekendRanges } from '@foundation/src/domain/scheduling/weekend-ranges';

const { mockUseSchedulingSettings, mockUseAvailabilityEvents } = vi.hoisted(() => ({
  mockUseSchedulingSettings: vi.fn((_?: unknown): { data: unknown } => ({ data: null })),
  mockUseAvailabilityEvents: vi.fn((_?: unknown): { data: unknown } => ({ data: [] })),
}));

vi.mock('@foundation/src/hooks/useScheduling', () => ({
  useSchedulingSettings: (arg?: unknown) => mockUseSchedulingSettings(arg),
  useAvailabilityEvents: (arg?: unknown) => mockUseAvailabilityEvents(arg),
}));

vi.mock('@foundation/src/domain/scheduling/recurrence', () => ({
  expandRecurrence: vi.fn(() => []),
}));

vi.mock('@foundation/src/domain/scheduling/weekend-ranges', () => ({
  generateWeekendRanges: vi.fn(() => []),
}));

const ANCHOR = new Date('2026-12-10T00:00:00Z');

function shutdown(id: string, enabled: boolean) {
  return {
    id,
    siteId: 'site-1',
    title: 'Shutdown',
    eventType: 'shutdown',
    defaultEffect: 'closed',
    startTs: '2026-12-24T00:00:00.000Z',
    endTs: '2026-12-26T00:00:00.000Z',
    isRecurring: false,
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
    mockUseAvailabilityEvents.mockReturnValue({ data: [shutdown('event-1', true)] });
    mockUseSchedulingSettings.mockReturnValue({ data: { timeZone: 'America/New_York', weekendsEnabled: true } });

    renderHook(() => useOffTimeRanges('site-1', ANCHOR));

    expect(vi.mocked(expandRecurrence)).toHaveBeenCalledTimes(1);
    expect(vi.mocked(expandRecurrence).mock.calls[0][3]).toBe('America/New_York');
  });

  it('generates weekend ranges when weekends are disabled', () => {
    mockUseSchedulingSettings.mockReturnValue({ data: { timeZone: 'UTC', weekendsEnabled: false } });
    renderHook(() => useOffTimeRanges('site-1', ANCHOR));
    expect(vi.mocked(generateWeekendRanges)).toHaveBeenCalled();
  });

  it('skips weekend ranges when weekends are enabled', () => {
    mockUseSchedulingSettings.mockReturnValue({ data: { timeZone: 'UTC', weekendsEnabled: true } });
    renderHook(() => useOffTimeRanges('site-1', ANCHOR));
    expect(vi.mocked(generateWeekendRanges)).not.toHaveBeenCalled();
  });

  it('filters out disabled availability events', () => {
    mockUseAvailabilityEvents.mockReturnValue({ data: [shutdown('event-1', false), shutdown('event-2', true)] });
    mockUseSchedulingSettings.mockReturnValue({ data: { timeZone: 'UTC', weekendsEnabled: true } });

    renderHook(() => useOffTimeRanges('site-1', ANCHOR));

    expect(vi.mocked(expandRecurrence)).toHaveBeenCalledTimes(1);
    expect(vi.mocked(expandRecurrence).mock.calls[0][0]).toMatchObject({ id: 'event-2' });
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
