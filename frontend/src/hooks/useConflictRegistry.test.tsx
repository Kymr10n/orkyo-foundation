import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';

vi.mock('@foundation/src/lib/api/conflicts-api', () => ({
  getConflicts: vi.fn(() => Promise.resolve([])),
}));
vi.mock('@foundation/src/lib/api/request-api', () => ({
  getConflictedRequests: vi.fn(() => Promise.resolve([])),
}));

import { getConflicts } from '@foundation/src/lib/api/conflicts-api';
import { useConflictRegistry } from '@foundation/src/hooks/useConflictRegistry';

function createWrapper() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  );
}

describe('useConflictRegistry', () => {
  beforeEach(() => vi.clearAllMocks());

  it('queries the all-time registry (no window) by default', async () => {
    renderHook(() => useConflictRegistry(), { wrapper: createWrapper() });
    await waitFor(() => expect(getConflicts).toHaveBeenCalled());
    expect(getConflicts).toHaveBeenCalledWith(undefined);
  });

  it('passes the visible window to getConflicts when from/to are supplied', async () => {
    const from = new Date('2026-05-01T00:00:00Z');
    const to = new Date('2026-05-08T00:00:00Z');
    renderHook(() => useConflictRegistry({ from, to }), { wrapper: createWrapper() });
    await waitFor(() => expect(getConflicts).toHaveBeenCalled());
    expect(getConflicts).toHaveBeenCalledWith({ from, to });
  });

  it('does not query when enabled is false (tab computes its own conflicts)', async () => {
    renderHook(() => useConflictRegistry({ enabled: false }), { wrapper: createWrapper() });
    // Give react-query a tick; the query function must never run.
    await new Promise((r) => setTimeout(r, 20));
    expect(getConflicts).not.toHaveBeenCalled();
  });

  it('maps the response into a requestId → conflicts map', async () => {
    vi.mocked(getConflicts).mockResolvedValue([
      { requestId: 'r1', conflicts: [{ id: 'c1' }] as never },
    ]);
    const { result } = renderHook(() => useConflictRegistry(), { wrapper: createWrapper() });
    await waitFor(() => expect(result.current.conflictsByRequest.size).toBe(1));
    expect(result.current.conflictsByRequest.get('r1')).toHaveLength(1);
  });
});
