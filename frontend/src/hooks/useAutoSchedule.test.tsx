import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { toast } from 'sonner';
import { usePreviewAutoSchedule, useApplyAutoSchedule, useAutoScheduleAvailable } from '@foundation/src/hooks/useAutoSchedule';
import { createTestQueryClient, createTestQueryWrapper } from '@foundation/src/test-utils';
import { applyAutoSchedule } from '@foundation/src/lib/api/auto-schedule-api';
import { REQUEST_DERIVED_QUERY_KEYS } from '@foundation/src/lib/core/invalidate-request-data';

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

vi.mock('@foundation/src/lib/api/auto-schedule-api', () => ({
  previewAutoSchedule: vi.fn(() => Promise.resolve({ assignments: [] })),
  applyAutoSchedule: vi.fn(() => Promise.resolve({ applied: 3 })),
}));

const { mockUseAuth, mockUseTenantSettings } = vi.hoisted(() => ({
  mockUseAuth: vi.fn(() => ({ membership: { tier: 'professional' } })),
  mockUseTenantSettings: vi.fn(() => ({
    data: { settings: [{ key: 'scheduling.auto_schedule_enabled', currentValue: 'True' }] },
  })),
}));

vi.mock('@foundation/src/contexts/AuthContext', () => ({ useAuth: mockUseAuth }));
vi.mock('@foundation/src/hooks/useTenantSettings', () => ({ useTenantSettings: mockUseTenantSettings }));

describe('usePreviewAutoSchedule', () => {
  it('returns a mutation', () => {
    const { result } = renderHook(() => usePreviewAutoSchedule(), { wrapper: createTestQueryWrapper() });
    expect(result.current.mutateAsync).toBeDefined();
    expect(result.current.isPending).toBe(false);
  });
});

describe('useApplyAutoSchedule', () => {
  const request = { siteId: 's1', horizonStart: '2026-01-01', horizonEnd: '2026-04-01' };

  beforeEach(() => vi.clearAllMocks());

  it('sends only the request and toasts the previewed count', async () => {
    const { spy, wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useApplyAutoSchedule(), { wrapper });

    await act(() => result.current.mutateAsync({ request, scheduledCount: 2 }));

    expect(vi.mocked(applyAutoSchedule).mock.calls[0][0]).toEqual(request);
    expect(toast.success).toHaveBeenCalledWith('Scheduled 2 requests');
    for (const queryKey of REQUEST_DERIVED_QUERY_KEYS) {
      expect(spy).toHaveBeenCalledWith({ queryKey, exact: false });
    }
  });

  it('says "1 request" and falls back when nothing was placed', async () => {
    const { wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useApplyAutoSchedule(), { wrapper });

    await act(() => result.current.mutateAsync({ request, scheduledCount: 1 }));
    expect(toast.success).toHaveBeenLastCalledWith('Scheduled 1 request');
    await act(() => result.current.mutateAsync({ request, scheduledCount: 0 }));
    expect(toast.success).toHaveBeenLastCalledWith('Auto-schedule applied');
  });

  it('leaves a failure to the dialog: no error toast', async () => {
    vi.mocked(applyAutoSchedule).mockRejectedValueOnce(new Error('stale'));
    const { wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useApplyAutoSchedule(), { wrapper });

    await act(() =>
      result.current.mutateAsync({ request, scheduledCount: 1 }).catch(() => undefined),
    );
    expect(toast.error).not.toHaveBeenCalled();
    expect(toast.success).not.toHaveBeenCalled();
  });
});

describe('useAutoScheduleAvailable', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Restore defaults for each test
    mockUseAuth.mockReturnValue({ membership: { tier: 'professional' } });
    mockUseTenantSettings.mockReturnValue({
      data: { settings: [{ key: 'scheduling.auto_schedule_enabled', currentValue: 'True' }] },
    });
  });

  it('returns true for Professional tier with setting enabled', () => {
    const { result } = renderHook(() => useAutoScheduleAvailable(), { wrapper: createTestQueryWrapper() });
    expect(result.current).toBe(true);
  });

  it('returns true for Enterprise tier with setting enabled', () => {
    mockUseAuth.mockReturnValue({ membership: { tier: 'enterprise' } });
    const { result } = renderHook(() => useAutoScheduleAvailable(), { wrapper: createTestQueryWrapper() });
    expect(result.current).toBe(true);
  });

  it('returns false for Free tier even when setting is enabled', () => {
    mockUseAuth.mockReturnValue({ membership: { tier: 'Free' } });
    const { result } = renderHook(() => useAutoScheduleAvailable(), { wrapper: createTestQueryWrapper() });
    expect(result.current).toBe(false);
  });

  it('returns false when setting value is "false"', () => {
    mockUseTenantSettings.mockReturnValue({
      data: { settings: [{ key: 'scheduling.auto_schedule_enabled', currentValue: 'false' }] },
    });
    const { result } = renderHook(() => useAutoScheduleAvailable(), { wrapper: createTestQueryWrapper() });
    expect(result.current).toBe(false);
  });

  it('accepts lowercase "true" as enabled', () => {
    mockUseTenantSettings.mockReturnValue({
      data: { settings: [{ key: 'scheduling.auto_schedule_enabled', currentValue: 'true' }] },
    });
    const { result } = renderHook(() => useAutoScheduleAvailable(), { wrapper: createTestQueryWrapper() });
    expect(result.current).toBe(true);
  });

  it('returns false when setting key is absent', () => {
    mockUseTenantSettings.mockReturnValue({ data: { settings: [] } });
    const { result } = renderHook(() => useAutoScheduleAvailable(), { wrapper: createTestQueryWrapper() });
    expect(result.current).toBe(false);
  });

  it('returns false when membership is null (unauthenticated)', () => {
    mockUseAuth.mockReturnValue({ membership: null as unknown as { tier: string } });
    const { result } = renderHook(() => useAutoScheduleAvailable(), { wrapper: createTestQueryWrapper() });
    expect(result.current).toBe(false);
  });
});
