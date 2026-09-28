import { describe, it, expect, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';

vi.mock('@foundation/src/lib/api/conflicts-api', () => ({
  getConflicts: vi.fn(() => Promise.resolve([])),
}));
vi.mock('@foundation/src/lib/api/request-api', () => ({
  getConflictedRequests: vi.fn(() => Promise.resolve([])),
}));

import { getConflicts } from '@foundation/src/lib/api/conflicts-api';
import { useConflictRegistry } from '@foundation/src/hooks/useConflictRegistry';
import { createTestQueryWrapper } from '@foundation/src/test-utils';

describe('useConflictRegistry', () => {
  it('queries the all-time registry (no window) by default', async () => {
    renderHook(() => useConflictRegistry(), { wrapper: createTestQueryWrapper() });
    await waitFor(() => expect(getConflicts).toHaveBeenCalled());
    expect(getConflicts).toHaveBeenCalledWith(undefined);
  });

  it('passes the visible window to getConflicts when from/to are supplied', async () => {
    const from = new Date('2026-05-01T00:00:00Z');
    const to = new Date('2026-05-08T00:00:00Z');
    renderHook(() => useConflictRegistry({ from, to }), { wrapper: createTestQueryWrapper() });
    await waitFor(() => expect(getConflicts).toHaveBeenCalled());
    expect(getConflicts).toHaveBeenCalledWith({ from, to });
  });

  it('does not query when enabled is false (tab computes its own conflicts)', async () => {
    renderHook(() => useConflictRegistry({ enabled: false }), { wrapper: createTestQueryWrapper() });
    // No wait: a query with enabled:false is never scheduled, so there is no moment at which it
    // could start. Sleeping only moves the assertion later, it does not make it stronger.
    expect(getConflicts).not.toHaveBeenCalled();
  });

  it('maps the response into a requestId → conflicts map', async () => {
    vi.mocked(getConflicts).mockResolvedValue([
      { requestId: 'r1', conflicts: [{ id: 'c1' }] as never },
    ]);
    const { result } = renderHook(() => useConflictRegistry(), { wrapper: createTestQueryWrapper() });
    await waitFor(() => expect(result.current.conflictsByRequest.size).toBe(1));
    expect(result.current.conflictsByRequest.get('r1')).toHaveLength(1);
  });
});
