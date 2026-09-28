import { describe, it, expect, vi } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { toast } from 'sonner';
import * as securityApi from '@foundation/src/lib/api/security-api';
import { createTestQueryClient } from '@foundation/src/test-utils';
import * as tenantAccountApi from '@foundation/src/lib/api/tenant-account-api';
import { useRequestEmailChange, useTenantMemberships } from './useAccount';

vi.mock('@foundation/src/lib/api/security-api');
vi.mock('@foundation/src/lib/api/tenant-account-api');

describe('useRequestEmailChange', () => {
  it('reports a failure inline only: no error toast next to the alert', async () => {
    vi.mocked(securityApi.requestEmailChange).mockRejectedValue(new Error('Email already in use'));
    const { wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useRequestEmailChange(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync('new@example.com').catch(() => {});
    });

    await waitFor(() => expect(result.current.error?.message).toBe('Email already in use'));
    expect(toast.error).not.toHaveBeenCalled();
  });
});

describe('useTenantMemberships', () => {
  it('clears the previous load failure once a reload succeeds', async () => {
    vi.mocked(tenantAccountApi.getTenantMemberships)
      .mockRejectedValueOnce(new Error('Network down'))
      .mockResolvedValueOnce([{ tenantId: 't-1' } as never]);
    const { result } = renderHook(() => useTenantMemberships());

    await waitFor(() => expect(result.current.error).toBe('Network down'));
    expect(result.current.loading).toBe(false);

    await act(() => result.current.reload());

    expect(result.current.error).toBeNull();
    expect(result.current.memberships).toEqual([{ tenantId: 't-1' }]);
  });
});
