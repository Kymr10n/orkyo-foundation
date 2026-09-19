import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { toast } from 'sonner';
import { createTestQueryWrapper } from '@foundation/src/test-utils';
import {
  useDeleteAiCredential,
  useRevokeAiAllowance,
  useSaveAiAllowance,
  useSaveAiCredential,
  useSaveAiDailyLimits,
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
}));

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

/**
 * The AI admin mutations declare their feedback in `meta`, so the settings page no longer
 * toasts by hand. These pin the messages at the source the MutationCache reads.
 */
describe('useAiAssistant mutation feedback', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('toasts the daily-limits outcome from meta', async () => {
    vi.mocked(aiApi.saveAiDailyLimits).mockResolvedValue(undefined);
    const { result } = renderHook(() => useSaveAiDailyLimits(), { wrapper: createTestQueryWrapper({ feedback: true }) });

    await result.current.mutateAsync({ userDailyTurns: 1, tenantDailyTurns: null });

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Daily limits saved'));
  });

  it('toasts the daily-limits failure from meta', async () => {
    vi.mocked(aiApi.saveAiDailyLimits).mockRejectedValue(new Error('nope'));
    const { result } = renderHook(() => useSaveAiDailyLimits(), { wrapper: createTestQueryWrapper({ feedback: true }) });

    await expect(result.current.mutateAsync({ userDailyTurns: 1, tenantDailyTurns: null })).rejects.toThrow('nope');

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith('Could not save the daily limits', expect.anything()),
    );
    expect(toast.success).not.toHaveBeenCalled();
  });

  it('save credential toasts its success message from meta', async () => {
    vi.mocked(aiApi.saveAiCredential).mockResolvedValue({
      configured: true,
      provider: 'anthropic',
      keyHint: 'x',
      updatedAt: null,
      lastVerifiedAt: null,
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
      expect(toast.success).toHaveBeenCalledWith('AI key removed. The assistant is switched off for this workspace.'),
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
