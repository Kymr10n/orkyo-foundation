import { beforeEach, describe, expect, it, vi } from 'vitest';
import type * as ApiUtils from '../core/api-utils';
import {
  getResources,
  getResource,
  createResource,
  updateResource,
  deleteResource,
  type ResourceInfo,
  type ResourcesResponse,
} from './resources-api';

vi.mock('@foundation/src/contexts/AuthContext', () => ({
  getAuthTokenSync: () => null,
  getTenantSlugSync: () => null,
}));

vi.mock('@foundation/src/lib/core/csrf', () => ({
  getCsrfToken: () => 'test-csrf-token',
  CSRF_HEADER_NAME: 'X-CSRF-Token',
  isMutatingMethod: (m: string) =>
    ['POST', 'PUT', 'PATCH', 'DELETE'].includes(m.toUpperCase()),
}));

vi.mock('@foundation/src/config/runtime', () => ({
  runtimeConfig: { apiBaseUrl: 'http://localhost:5000', baseDomain: '' },
}));

vi.mock('../core/api-utils', async (importOriginal) => {
  const actual = await importOriginal<typeof ApiUtils>();
  return {
    ...actual,
    handleApiError: vi.fn().mockImplementation(async (response: Response) => {
      const text = (await response.text?.()) || `Error ${(response as Response).status}`;
      throw new Error(text);
    }),
    API_BASE_URL: 'http://localhost:5000',
  };
});

const mockFetch = vi.fn();
global.fetch = mockFetch;

const mockResource: ResourceInfo = {
  id: 'res-1',
  resourceTypeId: 'rt-person',
  resourceTypeKey: 'person',
  name: 'Alice',
  allocationMode: 'Exclusive',
  baseAvailabilityPercent: 100,
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

const mockResponse: ResourcesResponse = {
  data: [mockResource],
  total: 1,
  page: 1,
  pageSize: 50,
};

describe('resources-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('getResources', () => {
    it('fetches resources with no filter', async () => {
      mockFetch.mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(mockResponse) });
      const result = await getResources();
      expect(result).toEqual(mockResponse);
      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/api/resources'),
        expect.any(Object),
      );
    });

    it('passes resourceTypeKey filter as query param', async () => {
      mockFetch.mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(mockResponse) });
      await getResources({ resourceTypeKey: 'person', isActive: true });
      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('resourceTypeKey=person'),
        expect.any(Object),
      );
    });
  });

  describe('getResource', () => {
    it('fetches a single resource by id', async () => {
      mockFetch.mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(mockResource) });
      const result = await getResource('res-1');
      expect(result).toEqual(mockResource);
      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/api/resources/res-1'),
        expect.any(Object),
      );
    });
  });

  describe('createResource', () => {
    it('posts a new resource and returns the created resource', async () => {
      mockFetch.mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(mockResource) });
      const request = {
        resourceTypeKey: 'person',
        name: 'Alice',
        allocationMode: 'Exclusive',
      };
      const result = await createResource(request);
      expect(result).toEqual(mockResource);
      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/api/resources'),
        expect.objectContaining({ method: 'POST' }),
      );
    });
  });

  describe('updateResource', () => {
    it('puts resource updates and returns the updated resource', async () => {
      const updated = { ...mockResource, name: 'Alice Updated' };
      mockFetch.mockResolvedValueOnce({ ok: true, json: () => Promise.resolve(updated) });
      const result = await updateResource('res-1', { name: 'Alice Updated' });
      expect(result).toEqual(updated);
      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/api/resources/res-1'),
        expect.objectContaining({ method: 'PUT' }),
      );
    });
  });

  describe('deleteResource', () => {
    it('sends a DELETE request for the resource', async () => {
      mockFetch.mockResolvedValueOnce({ ok: true, status: 204, text: () => Promise.resolve('') });
      await deleteResource('res-1');
      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/api/resources/res-1'),
        expect.objectContaining({ method: 'DELETE' }),
      );
    });
  });
});
