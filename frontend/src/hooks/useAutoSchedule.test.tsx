import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { usePreviewAutoSchedule, useApplyAutoSchedule, useAutoScheduleAvailable } from '@foundation/src/hooks/useAutoSchedule';

vi.mock('@foundation/src/lib/api/auto-schedule-api', () => ({
  previewAutoSchedule: vi.fn(() => Promise.resolve({ assignments: [] })),
  applyAutoSchedule: vi.fn(() => Promise.resolve({ applied: 3 })),
}));

vi.mock('@foundation/src/contexts/AuthContext', () => ({
  useAuth: () => ({
    membership: { tier: 'Professional' },
  }),
}));

vi.mock('@foundation/src/hooks/useTenantSettings', () => ({
  useTenantSettings: () => ({
    data: {
      settings: [
        { key: 'scheduling.auto_schedule_enabled', currentValue: 'True' },
      ],
    },
  }),
}));

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
  });

  it('returns true for Professional tier with setting enabled', () => {
    const { result } = renderHook(() => useAutoScheduleAvailable(), { wrapper: createWrapper() });
    expect(result.current).toBe(true);
  });
});
