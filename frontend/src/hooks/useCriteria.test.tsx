/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import {
  useCriteria,
  useCreateCriterion,
  useUpdateCriterion,
  useDeleteCriterion
} from './useCriteria';
import * as criteriaApi from '@foundation/src/lib/api/criteria-api';
import type { Criterion } from '@foundation/src/types/criterion';
import { createTestQueryWrapper, createTestQueryClientWithSpy } from '@foundation/src/test-utils';

vi.mock('@foundation/src/lib/api/criteria-api');

const mockCriterion: Criterion = {
  id: 'criterion-1',
  name: 'capacity',
  description: 'Room capacity',
  dataType: 'Number',
  unit: 'people',
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-01-01T00:00:00Z',
};

describe('useCriteria', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

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
      const { spy, wrapper } = createTestQueryClientWithSpy();

      vi.mocked(criteriaApi.createCriterion).mockResolvedValue(mockCriterion);

      const { result } = renderHook(() => useCreateCriterion(), { wrapper });

      await result.current.mutateAsync({
        name: 'capacity',
        description: 'Room capacity',
        dataType: 'Number',
        unit: 'people',
      });

      await waitFor(() => {
        expect(spy).toHaveBeenCalledWith({ queryKey: ['criteria'] });
        expect(spy).toHaveBeenCalledWith({ queryKey: ['requests'] });
      });
    });
  });

  describe('useUpdateCriterion', () => {
    it('updates a criterion and invalidates caches', async () => {
      const updatedCriterion = { ...mockCriterion, description: 'Updated' };
      vi.mocked(criteriaApi.updateCriterion).mockResolvedValue(updatedCriterion);

      const { result } = renderHook(() => useUpdateCriterion(), {
        wrapper: createTestQueryWrapper(),
      });

      await result.current.mutateAsync({
        id: 'criterion-1',
        data: {
          description: 'Updated',
        },
      });

      expect(criteriaApi.updateCriterion).toHaveBeenCalledWith('criterion-1', expect.any(Object));
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
