import { describe, expect, it, vi } from 'vitest';
import * as apiClient from '../core/api-client';
import { getResourceStatus } from './resource-status-api';

vi.mock('../core/api-client');

describe('resource-status-api', () => {
  it('reads the status of one resource', async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue({ resourceId: 'r1' });

    await getResourceStatus('r1');

    expect(apiClient.apiGet).toHaveBeenCalledWith('/api/resources/r1/status');
  });
});
