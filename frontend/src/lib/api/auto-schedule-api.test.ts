import { describe, it, expect, vi, beforeEach } from 'vitest';
import { previewAutoSchedule, applyAutoSchedule } from './auto-schedule-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

describe('auto-schedule-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('previewAutoSchedule posts to the preview endpoint', async () => {
    const mockResponse = {
      solverUsed: 'Greedy',
      status: 'Optimal',
      score: { scheduledCount: 3, unscheduledCount: 0, priorityScore: 100 },
      assignments: [],
      unscheduled: [],
      diagnostics: [],
      fingerprint: 'abc123',
    };
    vi.mocked(apiClient.apiPost).mockResolvedValue(mockResponse);

    const request = { siteId: 's1', horizonStart: '2024-01-01', horizonEnd: '2024-12-31' };
    const result = await previewAutoSchedule(request);

    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.AUTO_SCHEDULE_PREVIEW, request);
    expect(result.status).toBe('Optimal');
  });

  it('applyAutoSchedule posts to the apply endpoint', async () => {
    const mockResponse = { createdAssignments: 5, unscheduledCount: 1 };
    vi.mocked(apiClient.apiPost).mockResolvedValue(mockResponse);

    const request = { siteId: 's1', horizonStart: '2024-01-01', horizonEnd: '2024-12-31' };
    const result = await applyAutoSchedule(request);

    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.AUTO_SCHEDULE_APPLY, request);
    expect(result.createdAssignments).toBe(5);
  });
});
