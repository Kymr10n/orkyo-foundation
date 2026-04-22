import { describe, it, expect, vi, beforeEach } from 'vitest';
import { getGroupCapabilities, addGroupCapability, deleteGroupCapability } from './group-capability-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

describe('group-capability-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('getGroupCapabilities calls apiGet with group ID', async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue([]);
    await getGroupCapabilities('g1');
    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.groupCapabilities('g1'));
  });

  it('addGroupCapability calls apiPost', async () => {
    const req = { criterionId: 'c1', value: 42 };
    vi.mocked(apiClient.apiPost).mockResolvedValue({ id: 'cap-1', ...req });
    await addGroupCapability('g1', req);
    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.groupCapabilities('g1'), req);
  });

  it('deleteGroupCapability calls apiDelete', async () => {
    vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);
    await deleteGroupCapability('g1', 'cap-1');
    expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.groupCapability('g1', 'cap-1'));
  });
});
