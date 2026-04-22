import { describe, it, expect, vi, beforeEach } from 'vitest';
import { fetchRequests, fetchSpaces, scheduleRequest, type ScheduleRequestData } from './utilization-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

const mockRequest = {
  id: 'req-123',
  title: 'Team Meeting',
  status: 'pending',
  minimalDurationValue: 2,
  minimalDurationUnit: 'hours',
  spaceId: null,
  startTs: null,
  endTs: null,
};

const mockSpace = {
  id: 'space-456',
  name: 'Conference Room A',
  siteId: 'site-1',
  capacity: 10,
};

describe('utilization-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('fetchRequests', () => {
    it('calls apiGet with correct endpoint', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([mockRequest]);

      await fetchRequests();

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.REQUESTS);
    });

    it('returns requests with computed durationMin', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([mockRequest]);

      const result = await fetchRequests();

      expect(result).toHaveLength(1);
      expect(result[0].durationMin).toBe(120); // 2 hours = 120 minutes
    });

    it('converts minutes duration correctly', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([
        { ...mockRequest, minimalDurationValue: 30, minimalDurationUnit: 'minutes' },
      ]);

      const result = await fetchRequests();

      expect(result[0].durationMin).toBe(30);
    });

    it('converts hours duration correctly', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([
        { ...mockRequest, minimalDurationValue: 3, minimalDurationUnit: 'hours' },
      ]);

      const result = await fetchRequests();

      expect(result[0].durationMin).toBe(180);
    });

    it('converts days duration correctly', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([
        { ...mockRequest, minimalDurationValue: 1, minimalDurationUnit: 'days' },
      ]);

      const result = await fetchRequests();

      expect(result[0].durationMin).toBe(1440); // 24 * 60
    });

    it('converts weeks duration correctly', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([
        { ...mockRequest, minimalDurationValue: 1, minimalDurationUnit: 'weeks' },
      ]);

      const result = await fetchRequests();

      expect(result[0].durationMin).toBe(10080); // 7 * 24 * 60
    });

    it('converts months duration correctly', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([
        { ...mockRequest, minimalDurationValue: 1, minimalDurationUnit: 'months' },
      ]);

      const result = await fetchRequests();

      expect(result[0].durationMin).toBe(43200); // 30 * 24 * 60
    });

    it('converts years duration correctly', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([
        { ...mockRequest, minimalDurationValue: 1, minimalDurationUnit: 'years' },
      ]);

      const result = await fetchRequests();

      expect(result[0].durationMin).toBe(525600); // 365 * 24 * 60
    });

    it('handles unknown duration unit by returning raw value', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([
        { ...mockRequest, minimalDurationValue: 42, minimalDurationUnit: 'unknown' },
      ]);

      const result = await fetchRequests();

      expect(result[0].durationMin).toBe(42);
    });

    it('handles empty response', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([]);

      const result = await fetchRequests();

      expect(result).toHaveLength(0);
    });

    it('handles multiple requests', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([
        mockRequest,
        { ...mockRequest, id: 'req-456', minimalDurationValue: 1, minimalDurationUnit: 'days' },
      ]);

      const result = await fetchRequests();

      expect(result).toHaveLength(2);
      expect(result[0].durationMin).toBe(120);
      expect(result[1].durationMin).toBe(1440);
    });

    it('propagates API errors', async () => {
      const error = new Error('Network error');
      vi.mocked(apiClient.apiGet).mockRejectedValue(error);

      await expect(fetchRequests()).rejects.toThrow('Network error');
    });
  });

  describe('fetchSpaces', () => {
    it('calls apiGet with correct endpoint including siteId', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([mockSpace]);

      await fetchSpaces('site-123');

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.spaces('site-123'));
    });

    it('returns spaces array', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([mockSpace]);

      const result = await fetchSpaces('site-1');

      expect(result).toHaveLength(1);
      expect(result[0]).toEqual(mockSpace);
    });

    it('handles empty spaces list', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([]);

      const result = await fetchSpaces('site-1');

      expect(result).toHaveLength(0);
    });

    it('propagates API errors', async () => {
      const error = new Error('Site not found');
      vi.mocked(apiClient.apiGet).mockRejectedValue(error);

      await expect(fetchSpaces('invalid-site')).rejects.toThrow('Site not found');
    });
  });

  describe('scheduleRequest', () => {
    const scheduledRequest = {
      ...mockRequest,
      spaceId: 'space-456',
      startTs: '2024-01-15T09:00:00Z',
      endTs: '2024-01-15T11:00:00Z',
    };

    it('calls apiPatch with correct endpoint', async () => {
      vi.mocked(apiClient.apiPatch).mockResolvedValue(scheduledRequest);

      const data: ScheduleRequestData = {
        spaceId: 'space-456',
        startTs: '2024-01-15T09:00:00Z',
        endTs: '2024-01-15T11:00:00Z',
      };

      await scheduleRequest('req-123', data);

      expect(apiClient.apiPatch).toHaveBeenCalledWith(API_PATHS.requestSchedule('req-123'), data);
    });

    it('returns request with computed durationMin', async () => {
      vi.mocked(apiClient.apiPatch).mockResolvedValue(scheduledRequest);

      const data: ScheduleRequestData = { spaceId: 'space-456' };
      const result = await scheduleRequest('req-123', data);

      expect(result.durationMin).toBe(120);
    });

    it('handles partial update with only spaceId', async () => {
      vi.mocked(apiClient.apiPatch).mockResolvedValue(scheduledRequest);

      const data: ScheduleRequestData = { spaceId: 'space-456' };
      await scheduleRequest('req-123', data);

      expect(apiClient.apiPatch).toHaveBeenCalledWith(API_PATHS.requestSchedule('req-123'), {
        spaceId: 'space-456',
      });
    });

    it('handles partial update with only timestamps', async () => {
      vi.mocked(apiClient.apiPatch).mockResolvedValue(scheduledRequest);

      const data: ScheduleRequestData = {
        startTs: '2024-01-15T09:00:00Z',
        endTs: '2024-01-15T11:00:00Z',
      };
      await scheduleRequest('req-123', data);

      expect(apiClient.apiPatch).toHaveBeenCalledWith(API_PATHS.requestSchedule('req-123'), data);
    });

    it('handles null values to clear scheduling', async () => {
      vi.mocked(apiClient.apiPatch).mockResolvedValue(mockRequest);

      const data: ScheduleRequestData = {
        spaceId: null,
        startTs: null,
        endTs: null,
      };
      await scheduleRequest('req-123', data);

      expect(apiClient.apiPatch).toHaveBeenCalledWith(API_PATHS.requestSchedule('req-123'), data);
    });

    it('propagates API errors', async () => {
      const error = new Error('Request not found');
      vi.mocked(apiClient.apiPatch).mockRejectedValue(error);

      await expect(
        scheduleRequest('invalid-id', { spaceId: 'space-1' })
      ).rejects.toThrow('Request not found');
    });
  });
});
