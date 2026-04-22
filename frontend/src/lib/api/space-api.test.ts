import { describe, it, expect, vi, beforeEach } from 'vitest';
import { getSpaces, createSpace, updateSpace, deleteSpace } from './space-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

const mockSpace = {
  id: 'space-123',
  siteId: 'site-456',
  name: 'Conference Room A',
  description: 'Large meeting room',
  capacity: 20,
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-01-01T00:00:00Z',
};

describe('space-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('getSpaces', () => {
    it('calls apiGet with correct endpoint', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([mockSpace]);

      const result = await getSpaces('site-456');

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.spaces('site-456'));
      expect(result).toEqual([mockSpace]);
    });

    it('returns empty array when no spaces exist', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([]);

      const result = await getSpaces('site-456');

      expect(result).toEqual([]);
    });
  });

  describe('createSpace', () => {
    it('calls apiPost with correct endpoint and data', async () => {
      const createRequest = {
        name: 'Conference Room A',
        description: 'Large meeting room',
        isPhysical: true,
      };
      vi.mocked(apiClient.apiPost).mockResolvedValue(mockSpace);

      const result = await createSpace('site-456', createRequest);

      expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.spaces('site-456'), createRequest);
      expect(result).toEqual(mockSpace);
    });
  });

  describe('updateSpace', () => {
    it('calls apiPut with correct endpoint and data', async () => {
      const updateRequest = { name: 'Updated Room Name', description: 'Updated description' };
      const updatedSpace = { ...mockSpace, name: 'Updated Room Name', description: 'Updated description' };
      vi.mocked(apiClient.apiPut).mockResolvedValue(updatedSpace);

      const result = await updateSpace('site-456', 'space-123', updateRequest);

      expect(apiClient.apiPut).toHaveBeenCalledWith(
        API_PATHS.space('site-456', 'space-123'),
        updateRequest
      );
      expect(result).toEqual(updatedSpace);
    });
  });

  describe('deleteSpace', () => {
    it('calls apiDelete with correct endpoint', async () => {
      vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

      await deleteSpace('site-456', 'space-123');

      expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.space('site-456', 'space-123'));
    });
  });
});
