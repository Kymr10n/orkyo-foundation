import { describe, it, expect, vi, beforeEach } from 'vitest';
import { getCriteria, createCriterion, updateCriterion, deleteCriterion } from './criteria-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

const mockCriterion = {
  id: 'crit-123',
  name: 'Capacity',
  dataType: 'number',
  unit: 'people',
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-01-01T00:00:00Z',
};

describe('criteria-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('getCriteria', () => {
    it('calls apiGet with correct endpoint', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([mockCriterion]);

      const result = await getCriteria();

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.CRITERIA);
      expect(result).toEqual([mockCriterion]);
    });

    it('returns empty array when no criteria exist', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([]);

      const result = await getCriteria();

      expect(result).toEqual([]);
    });
  });

  describe('createCriterion', () => {
    it('calls apiPost with correct endpoint and data', async () => {
      const createRequest = { name: 'Capacity', dataType: 'Number' as const, unit: 'people' };
      vi.mocked(apiClient.apiPost).mockResolvedValue(mockCriterion);

      const result = await createCriterion(createRequest);

      expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.CRITERIA, createRequest);
      expect(result).toEqual(mockCriterion);
    });
  });

  describe('updateCriterion', () => {
    it('calls apiPut with correct endpoint and data', async () => {
      const updateRequest = { description: 'Updated Capacity' };
      const updatedCriterion = { ...mockCriterion, description: 'Updated Capacity' };
      vi.mocked(apiClient.apiPut).mockResolvedValue(updatedCriterion);

      const result = await updateCriterion('crit-123', updateRequest);

      expect(apiClient.apiPut).toHaveBeenCalledWith(API_PATHS.criterion('crit-123'), updateRequest);
      expect(result).toEqual(updatedCriterion);
    });
  });

  describe('deleteCriterion', () => {
    it('calls apiDelete with correct endpoint', async () => {
      vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

      await deleteCriterion('crit-123');

      expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.criterion('crit-123'));
    });
  });
});
