import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  getSpaceCapabilities,
  addSpaceCapability,
  deleteSpaceCapability,
} from './space-capability-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

const mockCapability = {
  id: 'cap-123',
  spaceId: 'space-456',
  criterionId: 'crit-789',
  value: 50,
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-01-01T00:00:00Z',
  criterion: {
    id: 'crit-789',
    name: 'Capacity',
    dataType: 'number',
    unit: 'people',
  },
};

describe('space-capability-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('getSpaceCapabilities', () => {
    it('calls apiGet with correct endpoint', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([mockCapability]);

      const result = await getSpaceCapabilities('site-1', 'space-456');

      expect(apiClient.apiGet).toHaveBeenCalledWith(
        API_PATHS.spaceCapabilities('site-1', 'space-456')
      );
      expect(result).toEqual([mockCapability]);
    });

    it('returns empty array when no capabilities exist', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([]);

      const result = await getSpaceCapabilities('site-1', 'space-456');

      expect(result).toEqual([]);
    });
  });

  describe('addSpaceCapability', () => {
    it('calls apiPost with correct endpoint and data', async () => {
      const createRequest = { criterionId: 'crit-789', value: 50 };
      vi.mocked(apiClient.apiPost).mockResolvedValue(mockCapability);

      const result = await addSpaceCapability('site-1', 'space-456', createRequest);

      expect(apiClient.apiPost).toHaveBeenCalledWith(
        API_PATHS.spaceCapabilities('site-1', 'space-456'),
        createRequest
      );
      expect(result).toEqual(mockCapability);
    });
  });

  describe('deleteSpaceCapability', () => {
    it('calls apiDelete with correct endpoint', async () => {
      vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

      await deleteSpaceCapability('site-1', 'space-456', 'cap-123');

      expect(apiClient.apiDelete).toHaveBeenCalledWith(
        API_PATHS.spaceCapability('site-1', 'space-456', 'cap-123')
      );
    });
  });
});
