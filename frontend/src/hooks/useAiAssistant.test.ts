import { describe, it, expect, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { toast } from 'sonner';
import { createTestQueryClient, createTestQueryWrapper } from '@foundation/src/test-utils';
import { qk } from '@foundation/src/lib/api/query-keys';
import { REQUEST_DERIVED_QUERY_KEYS } from '@foundation/src/lib/core/invalidate-request-data';
import { updateRequest } from '@foundation/src/lib/api/request-api';
import {
  useApplyAssistantProposal,
  useDeleteAiConversation,
  useDeleteAiCredential,
  useFetchAiConversation,
  useSaveAiConversation,
  useRevokeAiAllowance,
  useSaveAiAllowance,
  useSaveAiCredential,
  useSaveAiDailyLimits,
  useTestAiCredential,
} from './useAiAssistant';
import * as aiApi from '@foundation/src/lib/api/ai-api';

vi.mock('@foundation/src/lib/api/ai-api', () => ({
  saveAiCredential: vi.fn(),
  deleteAiCredential: vi.fn(),
  testAiCredential: vi.fn(),
  saveAiDailyLimits: vi.fn(),
  saveAiAllowance: vi.fn(),
  revokeAiAllowance: vi.fn(),
  getAiCredential: vi.fn(),
  getAiDailyLimits: vi.fn(),
  getAiStatus: vi.fn(),
  listAiAllowances: vi.fn(),
  getAiConversation: vi.fn(),
  saveAiConversation: vi.fn(),
  deleteAiConversation: vi.fn(),
}));

vi.mock('@foundation/src/lib/api/request-api', () => ({ updateRequest: vi.fn() }));

/**
 * The AI admin mutations declare their feedback in `meta`, so the settings page no longer
 * toasts by hand. These pin the messages at the source the MutationCache reads.
 */
describe('useAiAssistant mutation feedback', () => {
  it('toasts the daily-limits outcome from meta', async () => {
    vi.mocked(aiApi.saveAiDailyLimits).mockResolvedValue(undefined);
    const { result } = renderHook(() => useSaveAiDailyLimits(), { wrapper: createTestQueryWrapper({ feedback: true }) });

    await result.current.mutateAsync({ userDailyTurns: 1, tenantDailyTurns: null, privateChat: false });

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Daily limits saved'));
  });

  it('toasts the daily-limits failure from meta', async () => {
    vi.mocked(aiApi.saveAiDailyLimits).mockRejectedValue(new Error('nope'));
    const { result } = renderHook(() => useSaveAiDailyLimits(), { wrapper: createTestQueryWrapper({ feedback: true }) });

    await expect(result.current.mutateAsync({ userDailyTurns: 1, tenantDailyTurns: null, privateChat: false })).rejects.toThrow('nope');

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith('Could not save the daily limits', expect.anything()),
    );
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('toasts a failed key test from meta', async () => {
    vi.mocked(aiApi.testAiCredential).mockRejectedValue(new Error('offline'));
    const { result } = renderHook(() => useTestAiCredential(), { wrapper: createTestQueryWrapper({ feedback: true }) });

    await expect(result.current.mutateAsync()).rejects.toThrow('offline');

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith('Could not test the key', { description: 'offline' }));
  });

  it('save credential toasts its success message from meta', async () => {
    vi.mocked(aiApi.saveAiCredential).mockResolvedValue({
      configured: true,
      provider: 'anthropic',
      keyHint: 'x',
      updatedAt: null,
      lastVerifiedAt: null,
      rejectedAt: null,
    });
    const { result } = renderHook(() => useSaveAiCredential(), { wrapper: createTestQueryWrapper({ feedback: true }) });

    await result.current.mutateAsync('sk-ant-x');

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('AI key saved.'));
  });

  it('delete credential toasts its success message from meta', async () => {
    vi.mocked(aiApi.deleteAiCredential).mockResolvedValue(undefined);
    const { result } = renderHook(() => useDeleteAiCredential(), { wrapper: createTestQueryWrapper({ feedback: true }) });

    await result.current.mutateAsync();

    await waitFor(() =>
      expect(toast.success).toHaveBeenCalledWith('AI key removed. The assistant is switched off for this organization.'),
    );
  });

  it('save allowance toasts its success message from meta', async () => {
    vi.mocked(aiApi.saveAiAllowance).mockResolvedValue(undefined);
    const { result } = renderHook(() => useSaveAiAllowance(), { wrapper: createTestQueryWrapper({ feedback: true }) });

    await result.current.mutateAsync({ userId: 'u1', monthlyTokenLimit: 10 });

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Allowance updated.'));
  });

  it('revoke allowance toasts its success message from meta', async () => {
    vi.mocked(aiApi.revokeAiAllowance).mockResolvedValue(undefined);
    const { result } = renderHook(() => useRevokeAiAllowance(), { wrapper: createTestQueryWrapper({ feedback: true }) });

    await result.current.mutateAsync('u1');

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Access removed.'));
  });
});

describe('useApplyAssistantProposal', () => {
  it('refreshes every request-derived view after writing the change', async () => {
    vi.mocked(updateRequest).mockResolvedValue({} as never);
    const { spy, wrapper } = createTestQueryClient();
    const { result } = renderHook(() => useApplyAssistantProposal(), { wrapper });

    await result.current('req-1', { name: 'Renamed' });

    expect(updateRequest).toHaveBeenCalledWith('req-1', { name: 'Renamed' });
    for (const queryKey of REQUEST_DERIVED_QUERY_KEYS) {
      expect(spy).toHaveBeenCalledWith({ queryKey });
    }
  });
});

describe('saved conversations', () => {
  const body = { title: 'Any conflicts?', entries: [], transcript: [] };

  it('reads a conversation through the cache under its own key', async () => {
    const stored = { id: 'c1', ...body, updatedAt: '2026-01-01T00:00:00Z' };
    vi.mocked(aiApi.getAiConversation).mockResolvedValue(stored as never);
    const { queryClient, wrapper } = createTestQueryClient();
    const { result } = renderHook(() => useFetchAiConversation(), { wrapper });

    await expect(result.current('c1')).resolves.toEqual(stored);

    expect(queryClient.getQueryData(qk.ai.conversation('c1'))).toEqual(stored);
  });

  it('saving stores the body under the id and re-reads the list, silently', async () => {
    vi.mocked(aiApi.saveAiConversation).mockResolvedValue(undefined);
    const { spy, wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useSaveAiConversation(), { wrapper });

    await result.current.mutateAsync({ id: 'c1', ...body });

    expect(aiApi.saveAiConversation).toHaveBeenCalledWith('c1', body);
    await waitFor(() => expect(spy).toHaveBeenCalledWith({ queryKey: qk.ai.conversations(), exact: false }));
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('deleting re-reads the list', async () => {
    vi.mocked(aiApi.deleteAiConversation).mockResolvedValue(undefined);
    const { spy, wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useDeleteAiConversation(), { wrapper });

    await result.current.mutateAsync('c1');

    expect(aiApi.deleteAiConversation).toHaveBeenCalledWith('c1');
    await waitFor(() => expect(spy).toHaveBeenCalledWith({ queryKey: qk.ai.conversations(), exact: false }));
  });
});
