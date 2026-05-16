import { beforeEach, describe, expect, it, vi } from 'vitest';
import type * as ApiUtils from '../core/api-utils';
import { getResourceUtilization } from './resource-utilization-api';

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

describe('resource-utilization-api', () => {
  beforeEach(() => {
    mockFetch.mockReset();
  });

  describe('getResourceUtilization', () => {
    it('fetches utilization for a resource with date range and granularity', async () => {
      const mockUtilization = {
        from: '2026-01-01T00:00:00Z',
        to: '2026-01-08T00:00:00Z',
        granularity: 'day',
        buckets: [],
      };
      mockFetch.mockResolvedValueOnce({
        ok: true,
        json: () => Promise.resolve(mockUtilization),
      });
      const from = new Date('2026-01-01T00:00:00Z');
      const to = new Date('2026-01-08T00:00:00Z');
      const result = await getResourceUtilization('res-1', from, to, 'day');
      expect(result).toEqual(mockUtilization);
      expect(mockFetch).toHaveBeenCalledWith(
        expect.stringContaining('/api/resources/res-1/utilization'),
        expect.any(Object),
      );
      const calledUrl: string = mockFetch.mock.calls[0][0];
      expect(calledUrl).toContain('granularity=day');
      expect(calledUrl).toContain('from=');
      expect(calledUrl).toContain('to=');
    });
  });
});
