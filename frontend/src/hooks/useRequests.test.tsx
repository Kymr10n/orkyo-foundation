import { describe, it, expect, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { toast } from 'sonner';
import { createTestQueryClient } from '@foundation/src/test-utils';
import { makeRequest, makeRequestFormData } from '@foundation/src/test-utils/request-fixtures';
import { createRequest, updateRequest } from '@foundation/src/lib/api/request-api';
import { saveRequestVariables, useSaveRequest } from './useRequests';

vi.mock('@foundation/src/lib/api/request-api', () => ({
  createRequest: vi.fn(),
  updateRequest: vi.fn(),
}));

describe('saveRequestVariables', () => {
  it('is a create without a request and an update over the one being edited', () => {
    const form = makeRequestFormData();
    const editing = makeRequest({ id: 'req-1', planningMode: 'leaf', siteId: 'site-1' });

    expect(saveRequestVariables(form, null)).toEqual({ id: null, data: form });
    expect(saveRequestVariables(form, editing)).toEqual({ id: 'req-1', data: { form, previous: editing } });
  });
});

describe('useSaveRequest', () => {
  it('creates from the form and toasts the create message', async () => {
    vi.mocked(createRequest).mockResolvedValue(makeRequest());
    const { wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useSaveRequest(), { wrapper });

    await result.current.mutateAsync(saveRequestVariables(makeRequestFormData({ name: 'New' }), null));

    expect(createRequest).toHaveBeenCalledWith(expect.objectContaining({ name: 'New' }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Request created'));
  });

  it('updates the request the id names, leaving an unchanged planning mode out of the payload', async () => {
    vi.mocked(updateRequest).mockResolvedValue(makeRequest());
    const { wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useSaveRequest(), { wrapper });
    const editing = makeRequest({ id: 'req-1', planningMode: 'leaf' });

    await result.current.mutateAsync(saveRequestVariables(makeRequestFormData({ planningMode: 'leaf' }), editing));

    expect(updateRequest).toHaveBeenCalledWith('req-1', expect.not.objectContaining({ planningMode: 'leaf' }));
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Request updated'));
  });
});
