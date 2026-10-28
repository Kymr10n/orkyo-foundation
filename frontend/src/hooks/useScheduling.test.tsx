/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import {
  useSchedulingSettings,
  useUpsertSchedulingSettings,
  useDeleteSchedulingSettings,
  useAvailabilityEvents,
  useSaveAvailabilityEvent,
  useDeleteAvailabilityEvent,
} from './useScheduling';

vi.mock('@foundation/src/lib/api/scheduling-api', () => ({
  getSchedulingSettings: vi.fn(() => Promise.resolve({ siteId: 's1', timeZone: 'UTC' })),
  upsertSchedulingSettings: vi.fn(() => Promise.resolve({ siteId: 's1', timeZone: 'UTC' })),
  deleteSchedulingSettings: vi.fn(() => Promise.resolve()),
}));

vi.mock('@foundation/src/lib/api/availability-events-api', () => ({
  getAvailabilityEvents: vi.fn(() => Promise.resolve([])),
  createAvailabilityEvent: vi.fn(() => Promise.resolve({ id: 'evt-1' })),
  updateAvailabilityEvent: vi.fn(() => Promise.resolve({ id: 'evt-1' })),
  deleteAvailabilityEvent: vi.fn(() => Promise.resolve()),
}));

import {
  getSchedulingSettings,
  upsertSchedulingSettings,
  deleteSchedulingSettings,
} from '@foundation/src/lib/api/scheduling-api';
import {
  getAvailabilityEvents,
  createAvailabilityEvent,
  updateAvailabilityEvent,
  deleteAvailabilityEvent,
} from '@foundation/src/lib/api/availability-events-api';
import { createTestQueryClient, createTestQueryWrapper } from '@foundation/src/test-utils';
import { qk } from '@foundation/src/lib/api/query-keys';

function makeWrapper() {
  // Production-identical feedback MutationCache: the hooks declare meta.
  return createTestQueryWrapper({ feedback: true });
}

// ── useSchedulingSettings ─────────────────────────────────────────────────────

describe('useSchedulingSettings', () => {
  it('fetches settings when siteId is provided', async () => {
    const { result } = renderHook(() => useSchedulingSettings('s1'), { wrapper: makeWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(getSchedulingSettings).toHaveBeenCalledWith('s1');
    expect(result.current.data).toEqual({ siteId: 's1', timeZone: 'UTC' });
  });

  it('does not fetch when siteId is undefined', () => {
    const { result } = renderHook(() => useSchedulingSettings(undefined), { wrapper: makeWrapper() });
    expect(result.current.isLoading).toBe(false);
    expect(getSchedulingSettings).not.toHaveBeenCalled();
  });
});

// ── useUpsertSchedulingSettings ───────────────────────────────────────────────

describe('useUpsertSchedulingSettings', () => {
  const berlin = { timeZone: 'Europe/Berlin' } as any;
  const paris = { timeZone: 'Europe/Paris' } as any;

  it('sends the version the form was filled with', async () => {
    const { result } = renderHook(() => useUpsertSchedulingSettings('s1'), { wrapper: makeWrapper() });
    await act(() => result.current.mutateAsync({ settings: berlin, version: 4 }));
    expect(upsertSchedulingSettings).toHaveBeenCalledWith('s1', berlin, 4);
  });

  it('chains overlapping autosaves: each sends the version the previous one stored', async () => {
    // The form keeps version 4 while it autosaves. Sending 4 twice would refuse the user's own
    // second save with 412.
    vi.mocked(upsertSchedulingSettings)
      .mockResolvedValueOnce({ siteId: 's1', timeZone: 'Europe/Berlin', version: 5 } as any)
      .mockResolvedValueOnce({ siteId: 's1', timeZone: 'Europe/Paris', version: 6 } as any);
    const { result } = renderHook(() => useUpsertSchedulingSettings('s1'), { wrapper: makeWrapper() });

    await act(() => Promise.all([
      result.current.mutateAsync({ settings: berlin, version: 4 }),
      result.current.mutateAsync({ settings: paris, version: 4 }),
    ]));

    expect(upsertSchedulingSettings).toHaveBeenNthCalledWith(1, 's1', berlin, 4);
    expect(upsertSchedulingSettings).toHaveBeenNthCalledWith(2, 's1', paris, 5);
  });

  it('ignores the query cache: a refetch with someone else\'s version does not unlock the save', async () => {
    const { queryClient, wrapper } = createTestQueryClient({ feedback: true });
    queryClient.setQueryData(qk.scheduling.settings('s1'), { siteId: 's1', timeZone: 'UTC', version: 9 });
    const { result } = renderHook(() => useUpsertSchedulingSettings('s1'), { wrapper });

    await act(() => result.current.mutateAsync({ settings: berlin, version: 4 }));

    expect(upsertSchedulingSettings).toHaveBeenCalledWith('s1', berlin, 4);
  });

  it('starts over from a refilled form\'s version', async () => {
    // After a reset or a 412 the form is filled again; its new version wins over the chain.
    vi.mocked(upsertSchedulingSettings).mockResolvedValueOnce({ siteId: 's1', version: 5 } as any);
    const { result } = renderHook(() => useUpsertSchedulingSettings('s1'), { wrapper: makeWrapper() });

    await act(() => result.current.mutateAsync({ settings: berlin, version: 4 }));
    await act(() => result.current.mutateAsync({ settings: paris, version: 0 }));

    expect(upsertSchedulingSettings).toHaveBeenLastCalledWith('s1', paris, 0);
  });
});

// ── useDeleteSchedulingSettings ───────────────────────────────────────────────

describe('useDeleteSchedulingSettings', () => {
  it('calls deleteSchedulingSettings', async () => {
    const { result } = renderHook(() => useDeleteSchedulingSettings('s1'), { wrapper: makeWrapper() });
    await act(() => result.current.mutateAsync());
    expect(deleteSchedulingSettings).toHaveBeenCalledWith('s1');
  });
});

// ── useAvailabilityEvents ─────────────────────────────────────────────────────

describe('useAvailabilityEvents', () => {
  it('fetches availability events when siteId is provided', async () => {
    const { result } = renderHook(() => useAvailabilityEvents('s1'), { wrapper: makeWrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(getAvailabilityEvents).toHaveBeenCalledWith('s1');
    expect(result.current.data).toEqual([]);
  });

  it('does not fetch when siteId is undefined', () => {
    const { result } = renderHook(() => useAvailabilityEvents(undefined), { wrapper: makeWrapper() });
    expect(result.current.isLoading).toBe(false);
    expect(getAvailabilityEvents).not.toHaveBeenCalled();
  });
});

// ── useSaveAvailabilityEvent ──────────────────────────────────────────────────

describe('useSaveAvailabilityEvent', () => {
  it('creates when there is no id', async () => {
    const { result } = renderHook(() => useSaveAvailabilityEvent('s1'), { wrapper: makeWrapper() });
    await act(() => result.current.mutateAsync({ id: null, data: { title: 'Shutdown' } as any }));
    expect(createAvailabilityEvent).toHaveBeenCalledWith('s1', { title: 'Shutdown' });
  });

  it('updates the event the id names', async () => {
    const { result } = renderHook(() => useSaveAvailabilityEvent('s1'), { wrapper: makeWrapper() });
    await act(() => result.current.mutateAsync({ id: 'evt-1', data: { title: 'Updated' } as any }));
    expect(updateAvailabilityEvent).toHaveBeenCalledWith('s1', 'evt-1', { title: 'Updated' });
  });
});

// ── useDeleteAvailabilityEvent ────────────────────────────────────────────────

describe('useDeleteAvailabilityEvent', () => {
  it('calls deleteAvailabilityEvent with eventId', async () => {
    const { result } = renderHook(() => useDeleteAvailabilityEvent('s1'), { wrapper: makeWrapper() });
    await act(() => result.current.mutateAsync('evt-1'));
    expect(deleteAvailabilityEvent).toHaveBeenCalledWith('s1', 'evt-1');
  });
});
