import { describe, it, expect, vi, beforeEach } from 'vitest';
import { submitFeedback } from './feedback-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

describe('feedback-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('submitFeedback calls apiPost with correct data', async () => {
    const mockResponse = { id: 'fb-1', feedbackType: 'bug', title: 'Broken button', status: 'open', createdAt: '2024-01-01' };
    vi.mocked(apiClient.apiPost).mockResolvedValue(mockResponse);

    const req = { feedbackType: 'bug' as const, title: 'Broken button', description: 'Does not work' };
    const result = await submitFeedback(req);

    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.FEEDBACK, req);
    expect(result.id).toBe('fb-1');
  });
});
