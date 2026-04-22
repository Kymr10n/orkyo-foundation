import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  getRequests,
  createRequest,
  updateRequest,
  deleteRequest,
} from './request-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

const mockRequest = {
  id: 'req-123',
  name: 'Team Meeting',
  description: 'Weekly team sync',
  status: 'pending',
  spaceId: null,
  startTs: null,
  endTs: null,
  minimalDurationValue: 1,
  minimalDurationUnit: 'hours',
  requirements: [],
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-01-01T00:00:00Z',
};

describe('request-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('getRequests', () => {
    it('calls apiGet with correct endpoint', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([mockRequest]);

      const result = await getRequests();

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.REQUESTS);
      expect(result).toEqual([mockRequest]);
    });

    it('accepts includeRequirements parameter', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([mockRequest]);

      await getRequests(true);

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.REQUESTS);
    });

    it('returns empty array when no requests exist', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([]);

      const result = await getRequests();

      expect(result).toEqual([]);
    });
  });

  describe('createRequest', () => {
    it('calls apiPost with correct endpoint and data', async () => {
      const createReq = {
        name: 'Team Meeting',
        description: 'Weekly team sync',
        minimalDurationValue: 1,
        minimalDurationUnit: 'hours' as const,
      };
      vi.mocked(apiClient.apiPost).mockResolvedValue(mockRequest);

      const result = await createRequest(createReq);

      expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.REQUESTS, createReq);
      expect(result).toEqual(mockRequest);
    });
  });

  describe('updateRequest', () => {
    it('calls apiPut with correct endpoint and data', async () => {
      const updateReq = { name: 'Updated Meeting', status: 'in_progress' as const };
      const updatedRequest = { ...mockRequest, name: 'Updated Meeting', status: 'in_progress' as const };
      vi.mocked(apiClient.apiPut).mockResolvedValue(updatedRequest);

      const result = await updateRequest('req-123', updateReq);

      expect(apiClient.apiPut).toHaveBeenCalledWith(API_PATHS.request('req-123'), updateReq);
      expect(result).toEqual(updatedRequest);
    });
  });

  describe('deleteRequest', () => {
    it('calls apiDelete with correct endpoint', async () => {
      vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

      await deleteRequest('req-123');

      expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.request('req-123'));
    });
  });
});
