import { describe, it, expect, vi, beforeEach } from 'vitest';
import { getSites, createSite, updateSite, deleteSite } from './site-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

const mockSite = { id: 's1', name: 'HQ', address: '123 Main St' };

describe('site-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('getSites calls apiGet with SITES path', async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue([mockSite]);
    const result = await getSites();
    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.SITES);
    expect(result).toEqual([mockSite]);
  });

  it('createSite calls apiPost', async () => {
    const req = { name: 'New Site' };
    vi.mocked(apiClient.apiPost).mockResolvedValue({ ...mockSite, name: 'New Site' });
    await createSite(req as never);
    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.SITES, req);
  });

  it('updateSite calls apiPut with site ID', async () => {
    const req = { name: 'Updated Site' };
    vi.mocked(apiClient.apiPut).mockResolvedValue({ ...mockSite, name: 'Updated Site' });
    await updateSite('s1', req as never);
    expect(apiClient.apiPut).toHaveBeenCalledWith(API_PATHS.site('s1'), req);
  });

  it('deleteSite calls apiDelete with site ID', async () => {
    vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);
    await deleteSite('s1');
    expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.site('s1'));
  });
});
