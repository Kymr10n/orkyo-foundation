import { describe, expect, it, vi } from 'vitest';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import {
  getResources,
  getResource,
  createResource,
  updateResource,
  deleteResource,
  type ResourceInfo,
} from './resources-api';
import { pagedResult } from '@foundation/src/test-utils/paged-result';

vi.mock('../core/api-client');

const mockResource: ResourceInfo = {
  id: 'res-1',
  resourceTypeId: 'rt-person',
  resourceTypeKey: 'person',
  name: 'Alice',
  allocationMode: 'Exclusive',
  baseAvailabilityPercent: 100,
  isPhysical: false,
  capacity: 1,
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

const mockResponse = pagedResult([mockResource], { pageSize: 50 });

/** The params getResources handed to apiGet. */
const sentParams = () =>
  (vi.mocked(apiClient.apiGet).mock.calls[0][1] as { params: Record<string, unknown> }).params;

describe('resources-api', () => {
  describe('getResources', () => {
    it('fetches resources with no filter', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue(mockResponse);
      const result = await getResources();
      expect(result).toEqual(mockResponse);
      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.RESOURCES, expect.any(Object));
    });

    it('passes the type, active and site filters as params', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue(mockResponse);
      await getResources({ resourceTypeKey: 'person', isActive: true, siteId: 'site-1' });
      expect(sentParams()).toMatchObject({ resourceTypeKey: 'person', isActive: true, siteId: 'site-1' });
    });

    it('warns when an unpaged call comes back truncated', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
      const truncated = pagedResult([mockResource], { pageSize: 1, totalItems: 1500 });
      vi.mocked(apiClient.apiGet).mockResolvedValue(truncated);

      const result = await getResources({ isActive: true });

      expect(result.hasNextPage).toBe(true);
      expect(warn).toHaveBeenCalledWith('[WARN]', expect.stringContaining('1 of 1500'), expect.anything());
      warn.mockRestore();
    });

    it('does not warn for an explicit page request with more pages', async () => {
      const warn = vi.spyOn(console, 'warn').mockImplementation(() => {});
      vi.mocked(apiClient.apiGet).mockResolvedValue(pagedResult([mockResource], { pageSize: 1, totalItems: 3 }));

      await getResources({ page: 1, pageSize: 1 });

      expect(warn).not.toHaveBeenCalled();
      warn.mockRestore();
    });
  });

  it('getResource reads one resource by id', async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue(mockResource);
    expect(await getResource('res-1')).toEqual(mockResource);
    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.resource('res-1'));
  });

  it('createResource posts the request to the collection', async () => {
    vi.mocked(apiClient.apiPost).mockResolvedValue(mockResource);
    const request = { resourceTypeKey: 'person', name: 'Alice', allocationMode: 'Exclusive' };
    expect(await createResource(request)).toEqual(mockResource);
    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.RESOURCES, request);
  });

  it('updateResource puts the update to the item', async () => {
    const updated = { ...mockResource, name: 'Alice Updated' };
    vi.mocked(apiClient.apiPut).mockResolvedValue(updated);
    expect(await updateResource('res-1', { name: 'Alice Updated' })).toEqual(updated);
    expect(apiClient.apiPut).toHaveBeenCalledWith(API_PATHS.resource('res-1'), { name: 'Alice Updated' });
  });

  it('deleteResource deletes the item', async () => {
    vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);
    await deleteResource('res-1');
    expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.resource('res-1'));
  });
});
