/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { useSites, useCreateSite, useUpdateSite, useDeleteSite } from './useSites';
import * as siteApi from '@foundation/src/lib/api/site-api';
import type { Site } from '@foundation/src/lib/api/site-api';
import { createTestQueryWrapper, createTestQueryClientWithSpy } from '@foundation/src/test-utils';

vi.mock('@foundation/src/lib/api/site-api');

const mockSite: Site = {
  id: 'site-1',
  code: 'HQ',
  name: 'Headquarters',
  description: 'Main office',
  address: '123 Main St',
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-01-01T00:00:00Z',
};

describe('useSites', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('useSites query', () => {
    it('fetches all sites', async () => {
      const sites = [mockSite];
      vi.mocked(siteApi.getSites).mockResolvedValue(sites);

      const { result } = renderHook(() => useSites(), {
        wrapper: createTestQueryWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(sites);
      expect(siteApi.getSites).toHaveBeenCalled();
    });

    it('handles fetch errors', async () => {
      vi.mocked(siteApi.getSites).mockRejectedValue(new Error('Network error'));

      const { result } = renderHook(() => useSites(), {
        wrapper: createTestQueryWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(result.current.error).toEqual(new Error('Network error'));
    });
  });

  describe('useCreateSite', () => {
    it('creates a site and invalidates cache', async () => {
      const newSite = { ...mockSite, id: 'site-2', code: 'BR1' };
      vi.mocked(siteApi.createSite).mockResolvedValue(newSite);
      vi.mocked(siteApi.getSites).mockResolvedValue([mockSite, newSite]);

      const wrapper = createTestQueryWrapper();
      const { result: createResult } = renderHook(() => useCreateSite(), { wrapper });
      const { result: queryResult } = renderHook(() => useSites(), { wrapper });

      await waitFor(() => expect(queryResult.current.isSuccess).toBe(true));

      await createResult.current.mutateAsync({
        code: 'BR1',
        name: 'Branch Office',
        description: 'Branch location',
      });

      await waitFor(() => expect(queryResult.current.data).toHaveLength(2));
      expect(siteApi.createSite).toHaveBeenCalled();
    });
  });

  describe('useUpdateSite', () => {
    it('updates a site and invalidates cache', async () => {
      const updatedSite = { ...mockSite, name: 'Updated HQ' };
      vi.mocked(siteApi.updateSite).mockResolvedValue(updatedSite);
      vi.mocked(siteApi.getSites)
        .mockResolvedValueOnce([mockSite])  // Initial fetch
        .mockResolvedValueOnce([updatedSite]);  // After update

      const wrapper = createTestQueryWrapper();
      const { result: updateResult } = renderHook(() => useUpdateSite(), { wrapper });
      const { result: queryResult } = renderHook(() => useSites(), { wrapper });

      await waitFor(() => expect(queryResult.current.isSuccess).toBe(true));
      expect(queryResult.current.data?.[0].name).toBe('Headquarters');

      await updateResult.current.mutateAsync({
        id: 'site-1',
        data: { code: 'HQ', name: 'Updated HQ' },
      });

      await waitFor(() => expect(queryResult.current.data?.[0].name).toBe('Updated HQ'));
    });
  });

  describe('useDeleteSite', () => {
    it('deletes a site and invalidates all related caches', async () => {
      const { spy, wrapper } = createTestQueryClientWithSpy();

      vi.mocked(siteApi.deleteSite).mockResolvedValue();

      const { result } = renderHook(() => useDeleteSite(), { wrapper });

      await result.current.mutateAsync('site-1');

      await waitFor(() => {
        expect(spy).toHaveBeenCalledWith({ queryKey: ['sites'] });
        expect(spy).toHaveBeenCalledWith({ queryKey: ['spaces'] });
        expect(spy).toHaveBeenCalledWith({ queryKey: ['requests'] });
      });
    });
  });
});
