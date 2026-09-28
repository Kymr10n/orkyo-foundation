import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import { toast } from 'sonner';
import * as securityApi from '@foundation/src/lib/api/security-api';
import { createTestQueryClient } from '@foundation/src/test-utils';
import { useRequestEmailChange } from './useAccount';

vi.mock('@foundation/src/lib/api/security-api');
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

describe('useRequestEmailChange', () => {
  beforeEach(() => vi.clearAllMocks());

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
