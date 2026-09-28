import { describe, it, expect, vi } from 'vitest';
import { getBacklogRequests, scheduleRequest, type ScheduleRequestData } from './utilization-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import { spaceAssignment } from '@foundation/src/test-utils/request-fixtures';

vi.mock('../core/api-client');

const mockRequest = {
  id: 'req-123',
  title: 'Team Meeting',
  assignments: [],
  status: 'pending',
  minimalDurationValue: 2,
  minimalDurationUnit: 'hours',
  startTs: null,
  endTs: null,
};

describe('utilization-api', () => {
  describe('getBacklogRequests', () => {
    it('reads the unscheduled backlog and computes durationMin', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([mockRequest]);

      const result = await getBacklogRequests();

      expect(apiClient.apiGet).toHaveBeenCalledWith(`${API_PATHS.REQUESTS}?scheduled=false`);
      expect(result[0].durationMin).toBe(120);
    });
  });

  describe('scheduleRequest', () => {
    const scheduledRequest = {
      ...mockRequest,
      assignments: [spaceAssignment('space-456')],
      startTs: '2024-01-15T09:00:00Z',
      endTs: '2024-01-15T11:00:00Z',
    };

    it('calls apiPatch with correct endpoint', async () => {
      vi.mocked(apiClient.apiPatch).mockResolvedValue(scheduledRequest);

      const data: ScheduleRequestData = {
        resourceId: 'space-456',
        startTs: '2024-01-15T09:00:00Z',
        endTs: '2024-01-15T11:00:00Z',
      };

      await scheduleRequest('req-123', data);

      expect(apiClient.apiPatch).toHaveBeenCalledWith(API_PATHS.requestSchedule('req-123'), data);
    });

    it('returns request with computed durationMin', async () => {
      vi.mocked(apiClient.apiPatch).mockResolvedValue(scheduledRequest);

      const data: ScheduleRequestData = { resourceId: 'space-456' };
      const result = await scheduleRequest('req-123', data);

      expect(result.durationMin).toBe(120);
    });

    it('handles partial update with only resourceId', async () => {
      vi.mocked(apiClient.apiPatch).mockResolvedValue(scheduledRequest);

      const data: ScheduleRequestData = { resourceId: 'space-456' };
      await scheduleRequest('req-123', data);

      expect(apiClient.apiPatch).toHaveBeenCalledWith(API_PATHS.requestSchedule('req-123'), {
        resourceId: 'space-456',
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
        resourceId: null,
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
        scheduleRequest('invalid-id', { resourceId: 'space-1' })
      ).rejects.toThrow('Request not found');
    });
  });
});
