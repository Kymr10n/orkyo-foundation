/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import {
  useCriteria,
  useCreateCriterion,
  useUpdateCriterion,
  useDeleteCriterion,
  useUpdateCriterionApplicability,
} from './useCriteria';
import * as criteriaApi from '@foundation/src/lib/api/criteria-api';
import type { Criterion, ResourceTypeKey } from '@foundation/src/types/criterion';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactNode } from 'react';
import { createTestQueryWrapper, createTestQueryClientWithSpy } from '@foundation/src/test-utils';
import { createFeedbackMutationCache } from '@foundation/src/lib/core/query-client';

vi.mock('@foundation/src/lib/api/criteria-api');
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

// CRUD mutations invalidate through the meta-driven MutationCache; wire it so the spy fires.
function createFeedbackClientWithSpy() {
  const client: QueryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    mutationCache: createFeedbackMutationCache(() => client),
  });
  const spy = vi.spyOn(client, 'invalidateQueries');
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { client, spy, wrapper };
}

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
      const { spy, wrapper } = createFeedbackClientWithSpy();

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

  describe('useUpdateCriterionApplicability', () => {
    it('calls updateCriterionApplicability and invalidates criteria cache', async () => {
      const applicabilityResult = {
        criterionId: 'criterion-1',
        applicableToRequests: true,
        resourceTypeKeys: ['space', 'person'] as ResourceTypeKey[],
      };
      vi.mocked(criteriaApi.updateCriterionApplicability).mockResolvedValue(applicabilityResult);

      const { spy, wrapper } = createTestQueryClientWithSpy();
      const { result } = renderHook(() => useUpdateCriterionApplicability(), { wrapper });

      await result.current.mutateAsync({
        id: 'criterion-1',
        data: { resourceTypeKeys: ['space', 'person'] },
      });

      await waitFor(() => {
        expect(spy).toHaveBeenCalledWith({ queryKey: ['criteria'] });
      });
      expect(criteriaApi.updateCriterionApplicability).toHaveBeenCalledWith(
        'criterion-1',
        { resourceTypeKeys: ['space', 'person'] },
      );
    });
  });
});
