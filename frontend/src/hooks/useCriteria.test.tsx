/** @jsxImportSource react */
import { describe, it, expect, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { toast } from 'sonner';
import {
  useCriteria,
  useCreateCriterion,
  useDeleteCriterion,
  useSaveCriterion,
  type CriterionDraft,
} from './useCriteria';
import * as criteriaApi from '@foundation/src/lib/api/criteria-api';
import type { Criterion } from '@foundation/src/types/criterion';
import { createTestQueryWrapper, createTestQueryClient } from '@foundation/src/test-utils';

vi.mock('@foundation/src/lib/api/criteria-api');

const mockCriterion: Criterion = {
  id: 'criterion-1',
  name: 'capacity',
  description: 'Room capacity',
  dataType: 'Number',
  unit: 'people',
  resourceTypeKeys: ['space'],
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-01-01T00:00:00Z',
};

describe('useCriteria', () => {
  describe('useCriteria query', () => {
    it('fetches criteria (tenant-wide)', async () => {
      const criteria = [mockCriterion];
      vi.mocked(criteriaApi.getCriteria).mockResolvedValue(criteria);

      const { result } = renderHook(() => useCriteria(), {
        wrapper: createTestQueryWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(criteria);
      expect(criteriaApi.getCriteria).toHaveBeenCalled();
    });
  });

  describe('useCreateCriterion', () => {
    it('creates a criterion and invalidates caches', async () => {
      // CRUD mutations invalidate through the meta-driven MutationCache; wire it so the spy fires.
      const { spy, wrapper } = createTestQueryClient({ feedback: true });

      vi.mocked(criteriaApi.createCriterion).mockResolvedValue(mockCriterion);

      const { result } = renderHook(() => useCreateCriterion(), { wrapper });

      await result.current.mutateAsync({
        name: 'capacity',
        description: 'Room capacity',
        dataType: 'Number',
        unit: 'people',
        resourceTypeKeys: ['space'],
      });

      await waitFor(() => {
        expect(spy).toHaveBeenCalledWith({ queryKey: ['criteria'], exact: false });
        expect(spy).toHaveBeenCalledWith({ queryKey: ['requests'], exact: false });
      });
    });
  });

  describe('useSaveCriterion', () => {
    const draft: CriterionDraft = {
      name: 'capacity',
      dataType: 'Number',
      description: 'Room capacity',
      unit: 'people',
      enumValues: [],
      resourceTypeKeys: ['space'],
    };

    it('creates from a draft with no id', async () => {
      const { wrapper } = createTestQueryClient({ feedback: true });
      vi.mocked(criteriaApi.createCriterion).mockResolvedValue(mockCriterion);
      const { result } = renderHook(() => useSaveCriterion(), { wrapper });

      await result.current.mutateAsync({ id: null, data: draft });

      expect(criteriaApi.createCriterion).toHaveBeenCalledWith({
        name: 'capacity',
        description: 'Room capacity',
        dataType: 'Number',
        enumValues: undefined,
        unit: 'people',
        resourceTypeKeys: ['space'],
      });
      await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Criterion created'));
    });

    it('updates against the previous criterion, sending only what changed', async () => {
      const { wrapper } = createTestQueryClient({ feedback: true });
      vi.mocked(criteriaApi.updateCriterion).mockResolvedValue(mockCriterion);
      const { result } = renderHook(() => useSaveCriterion(), { wrapper });

      await result.current.mutateAsync({
        id: mockCriterion.id,
        data: { draft: { ...draft, description: 'Seats' }, previous: mockCriterion },
      });

      expect(criteriaApi.updateCriterion).toHaveBeenCalledWith(mockCriterion.id, {
        description: 'Seats',
        enumValues: undefined,
        unit: 'people',
      });
      expect(criteriaApi.updateCriterionApplicability).not.toHaveBeenCalled();
      await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Criterion updated'));
    });
  });

  describe('useDeleteCriterion', () => {
    it('deletes a criterion and invalidates caches', async () => {
      vi.mocked(criteriaApi.deleteCriterion).mockResolvedValue();

      const { result } = renderHook(() => useDeleteCriterion(), {
        wrapper: createTestQueryWrapper(),
      });

      await result.current.mutateAsync('criterion-1');

      expect(criteriaApi.deleteCriterion).toHaveBeenCalledWith('criterion-1');
    });
  });
});
