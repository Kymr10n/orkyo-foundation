import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { usePreviewAutoSchedule, useApplyAutoSchedule, useAutoScheduleAvailable } from '@foundation/src/hooks/useAutoSchedule';

vi.mock('@foundation/src/lib/api/auto-schedule-api', () => ({
  previewAutoSchedule: vi.fn(() => Promise.resolve({ assignments: [] })),
  applyAutoSchedule: vi.fn(() => Promise.resolve({ applied: 3 })),
}));

const { mockUseAuth, mockUseTenantSettings } = vi.hoisted(() => ({
  mockUseAuth: vi.fn(() => ({ membership: { tier: 'Professional' } })),
  mockUseTenantSettings: vi.fn(() => ({
    data: { settings: [{ key: 'scheduling.auto_schedule_enabled', currentValue: 'True' }] },
  })),
}));

vi.mock('@foundation/src/contexts/AuthContext', () => ({ useAuth: mockUseAuth }));
vi.mock('@foundation/src/hooks/useTenantSettings', () => ({ useTenantSettings: mockUseTenantSettings }));

function createWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

describe('usePreviewAutoSchedule', () => {
  it('returns a mutation', () => {
    const { result } = renderHook(() => usePreviewAutoSchedule(), { wrapper: createWrapper() });
    expect(result.current.mutateAsync).toBeDefined();
    expect(result.current.isPending).toBe(false);
  });
});

describe('useApplyAutoSchedule', () => {
  it('returns a mutation', () => {
    const { result } = renderHook(() => useApplyAutoSchedule(), { wrapper: createWrapper() });
    expect(result.current.mutateAsync).toBeDefined();
    expect(result.current.isPending).toBe(false);
  });
});

describe('useAutoScheduleAvailable', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Restore defaults for each test
    mockUseAuth.mockReturnValue({ membership: { tier: 'Professional' } });
    mockUseTenantSettings.mockReturnValue({
      data: { settings: [{ key: 'scheduling.auto_schedule_enabled', currentValue: 'True' }] },
    });
  });

  it('returns true for Professional tier with setting enabled', () => {
    const { result } = renderHook(() => useAutoScheduleAvailable(), { wrapper: createWrapper() });
    expect(result.current).toBe(true);
  });

  it('returns true for Enterprise tier with setting enabled', () => {
    mockUseAuth.mockReturnValue({ membership: { tier: 'Enterprise' } });
    const { result } = renderHook(() => useAutoScheduleAvailable(), { wrapper: createWrapper() });
    expect(result.current).toBe(true);
  });

  it('returns false for Free tier even when setting is enabled', () => {
    mockUseAuth.mockReturnValue({ membership: { tier: 'Free' } });
    const { result } = renderHook(() => useAutoScheduleAvailable(), { wrapper: createWrapper() });
    expect(result.current).toBe(false);
  });

  it('returns false when setting value is "false"', () => {
    mockUseTenantSettings.mockReturnValue({
      data: { settings: [{ key: 'scheduling.auto_schedule_enabled', currentValue: 'false' }] },
    });
    const { result } = renderHook(() => useAutoScheduleAvailable(), { wrapper: createWrapper() });
    expect(result.current).toBe(false);
  });

  it('accepts lowercase "true" as enabled', () => {
    mockUseTenantSettings.mockReturnValue({
      data: { settings: [{ key: 'scheduling.auto_schedule_enabled', currentValue: 'true' }] },
    });
    const { result } = renderHook(() => useAutoScheduleAvailable(), { wrapper: createWrapper() });
    expect(result.current).toBe(true);
  });

  it('returns false when setting key is absent', () => {
    mockUseTenantSettings.mockReturnValue({ data: { settings: [] } });
    const { result } = renderHook(() => useAutoScheduleAvailable(), { wrapper: createWrapper() });
    expect(result.current).toBe(false);
  });

  it('returns false when membership is null (unauthenticated)', () => {
    mockUseAuth.mockReturnValue({ membership: null as unknown as { tier: string } });
    const { result } = renderHook(() => useAutoScheduleAvailable(), { wrapper: createWrapper() });
    expect(result.current).toBe(false);
  });
});
