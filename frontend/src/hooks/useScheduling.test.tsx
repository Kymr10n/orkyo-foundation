/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, act, waitFor } from '@testing-library/react';
import {
  useSchedulingSettings,
  useUpsertSchedulingSettings,
  useDeleteSchedulingSettings,
  useAvailabilityEvents,
  useCreateAvailabilityEvent,
  useUpdateAvailabilityEvent,
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
import { createFeedbackTestQueryWrapper } from '@foundation/src/test-utils';

function makeWrapper() {
  // Production-identical feedback MutationCache: the hooks declare meta.
  return createFeedbackTestQueryWrapper();
}

// ── useSchedulingSettings ─────────────────────────────────────────────────────

describe('useSchedulingSettings', () => {
  beforeEach(() => vi.clearAllMocks());

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
  beforeEach(() => vi.clearAllMocks());

  it('calls upsertSchedulingSettings and invalidates cache', async () => {
    const { result } = renderHook(() => useUpsertSchedulingSettings('s1'), { wrapper: makeWrapper() });
    await act(() => result.current.mutateAsync({ timeZone: 'Europe/Berlin' } as any));
    expect(upsertSchedulingSettings).toHaveBeenCalledWith('s1', { timeZone: 'Europe/Berlin' });
  });
});

// ── useDeleteSchedulingSettings ───────────────────────────────────────────────

describe('useDeleteSchedulingSettings', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls deleteSchedulingSettings', async () => {
    const { result } = renderHook(() => useDeleteSchedulingSettings('s1'), { wrapper: makeWrapper() });
    await act(() => result.current.mutateAsync());
    expect(deleteSchedulingSettings).toHaveBeenCalledWith('s1');
  });
});

// ── useAvailabilityEvents ─────────────────────────────────────────────────────

describe('useAvailabilityEvents', () => {
  beforeEach(() => vi.clearAllMocks());

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

// ── useCreateAvailabilityEvent ────────────────────────────────────────────────

describe('useCreateAvailabilityEvent', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls createAvailabilityEvent with siteId', async () => {
    const { result } = renderHook(() => useCreateAvailabilityEvent('s1'), { wrapper: makeWrapper() });
    await act(() => result.current.mutateAsync({ title: 'Shutdown' } as any));
    expect(createAvailabilityEvent).toHaveBeenCalledWith('s1', { title: 'Shutdown' });
  });
});

// ── useUpdateAvailabilityEvent ────────────────────────────────────────────────

describe('useUpdateAvailabilityEvent', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls updateAvailabilityEvent with eventId and updates', async () => {
    const { result } = renderHook(() => useUpdateAvailabilityEvent('s1'), { wrapper: makeWrapper() });
    await act(() => result.current.mutateAsync({ eventId: 'evt-1', updates: { title: 'Updated' } as any }));
    expect(updateAvailabilityEvent).toHaveBeenCalledWith('s1', 'evt-1', { title: 'Updated' });
  });
});

// ── useDeleteAvailabilityEvent ────────────────────────────────────────────────

describe('useDeleteAvailabilityEvent', () => {
  beforeEach(() => vi.clearAllMocks());

  it('calls deleteAvailabilityEvent with eventId', async () => {
    const { result } = renderHook(() => useDeleteAvailabilityEvent('s1'), { wrapper: makeWrapper() });
    await act(() => result.current.mutateAsync('evt-1'));
    expect(deleteAvailabilityEvent).toHaveBeenCalledWith('s1', 'evt-1');
  });
});
