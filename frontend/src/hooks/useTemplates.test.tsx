import { describe, it, expect, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { toast } from 'sonner';
import { createTestQueryClient } from '@foundation/src/test-utils';
import { createTemplate, updateTemplate } from '@foundation/src/lib/api/template-api';
import { useSaveTemplate } from './useTemplates';

vi.mock('@foundation/src/lib/api/template-api', () => ({
  createTemplate: vi.fn(),
  updateTemplate: vi.fn(),
}));

const data = { name: 'Weekly review', entityType: 'request', durationValue: 1, durationUnit: 'hours' } as const;

describe('useSaveTemplate', () => {
  it('creates when the id is null and re-reads the list', async () => {
    vi.mocked(createTemplate).mockResolvedValue({} as never);
    const { spy, wrapper } = createTestQueryClient({ feedback: true });
    const onSuccess = vi.fn();
    const { result } = renderHook(() => useSaveTemplate('request', { onSuccess, onError: vi.fn() }), { wrapper });

    await result.current.mutateAsync({ id: null, data });

    expect(createTemplate).toHaveBeenCalledWith(data);
    expect(onSuccess).toHaveBeenCalled();
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Template created'));
    expect(spy).toHaveBeenCalledWith({ queryKey: ['templates-request'], exact: false });
  });

  it('updates the template the id names', async () => {
    vi.mocked(updateTemplate).mockResolvedValue({} as never);
    const { wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useSaveTemplate('request', { onSuccess: vi.fn(), onError: vi.fn() }), { wrapper });

    await result.current.mutateAsync({ id: 't1', data });

    expect(updateTemplate).toHaveBeenCalledWith('t1', data);
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Template updated'));
  });

  it('hands a failure to the caller instead of toasting it', async () => {
    vi.mocked(createTemplate).mockRejectedValue(new Error('nope'));
    const { wrapper } = createTestQueryClient({ feedback: true });
    const onError = vi.fn();
    const { result } = renderHook(() => useSaveTemplate('request', { onSuccess: vi.fn(), onError }), { wrapper });

    await expect(result.current.mutateAsync({ id: null, data })).rejects.toThrow('nope');

    expect(vi.mocked(onError).mock.calls[0][0]).toMatchObject({ message: 'nope' });
    expect(toast.error).not.toHaveBeenCalled();
  });
});
