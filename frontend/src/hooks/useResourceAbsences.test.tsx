import { describe, it, expect, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { toast } from 'sonner';
import { createTestQueryClient } from '@foundation/src/test-utils';
import {
  createResourceAbsence,
  updateResourceAbsence,
  type ResourceAbsenceInfo,
} from '@foundation/src/lib/api/resource-absences-api';
import { useSaveResourceAbsence } from './useResourceAbsences';

vi.mock('@foundation/src/lib/api/resource-absences-api', () => ({
  createResourceAbsence: vi.fn(),
  updateResourceAbsence: vi.fn(),
}));

const payload = {
  absenceType: 'vacation' as const,
  title: 'Leave',
  startTs: '2026-03-02T00:00:00.000Z',
  endTs: '2026-03-06T00:00:00.000Z',
};

const previous = {
  id: 'a1',
  resourceId: 'r1',
  ...payload,
  title: 'Old title',
  notes: 'Keep me',
  enabled: false,
} as ResourceAbsenceInfo;

describe('useSaveResourceAbsence', () => {
  it('adds an absence when the id is null and refreshes the views it changes', async () => {
    vi.mocked(createResourceAbsence).mockResolvedValue(previous);
    const { spy, wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useSaveResourceAbsence('r1'), { wrapper });

    await result.current.mutateAsync({ id: null, data: payload });

    expect(createResourceAbsence).toHaveBeenCalledWith('r1', payload);
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Absence added'));
    expect(spy).toHaveBeenCalledWith({ queryKey: ['resource-absences', 'r1'], exact: false });
    expect(spy).toHaveBeenCalledWith({ queryKey: ['conflicts'], exact: false });
  });

  it('rewrites the absence the id names, keeping its notes and enabled flag', async () => {
    vi.mocked(updateResourceAbsence).mockResolvedValue(previous);
    const { wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useSaveResourceAbsence('r1'), { wrapper });

    await result.current.mutateAsync({ id: 'a1', data: { payload, previous } });

    expect(updateResourceAbsence).toHaveBeenCalledWith('r1', 'a1', { ...payload, notes: 'Keep me', enabled: false });
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Absence updated'));
  });

  it('names the branch that failed in the error toast', async () => {
    vi.mocked(updateResourceAbsence).mockRejectedValue(new Error('overlap'));
    const { wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useSaveResourceAbsence('r1'), { wrapper });

    await expect(result.current.mutateAsync({ id: 'a1', data: { payload, previous } })).rejects.toThrow('overlap');

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith('Failed to update absence', { description: 'overlap' }),
    );
  });
});
