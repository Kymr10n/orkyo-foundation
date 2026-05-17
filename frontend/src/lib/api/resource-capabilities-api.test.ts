import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  getResourceCapabilities,
  upsertResourceCapability,
  deleteResourceCapability,
} from './resource-capabilities-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

const mockCapability = {
  id: 'cap-1',
  resourceId: 'res-1',
  criterionId: 'crit-1',
  value: true as const,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
  criterion: { id: 'crit-1', name: 'First-aid trained', dataType: 'Boolean' },
};

describe('resource-capabilities-api', () => {
  beforeEach(() => vi.clearAllMocks());

  it('getResourceCapabilities calls /api/resources/{id}/capabilities', async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue([mockCapability]);

    const result = await getResourceCapabilities('res-1');

    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.resourceCapabilities('res-1'));
    expect(result).toEqual([mockCapability]);
  });

  it('upsertResourceCapability POSTs criterionId + value', async () => {
    vi.mocked(apiClient.apiPost).mockResolvedValue(mockCapability);

    await upsertResourceCapability('res-1', { criterionId: 'crit-1', value: true });

    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.resourceCapabilities('res-1'), {
      criterionId: 'crit-1',
      value: true,
    });
  });

  it('deleteResourceCapability DELETEs capability path', async () => {
    vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

    await deleteResourceCapability('res-1', 'cap-1');

    expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.resourceCapability('res-1', 'cap-1'));
  });
});
