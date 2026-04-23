/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  useSpaces,
  useCreateSpace,
  useUpdateSpace,
  useDeleteSpace,
  useMoveSpace,
} from './useSpaces';
import * as spaceApi from '@foundation/src/lib/api/space-api';
import type { Space, SpaceGeometry } from '@foundation/src/types/space';
import { createTestQueryWrapper } from '@foundation/src/test-utils';

vi.mock('@foundation/src/lib/api/space-api');

const mockSpace: Space = {
  id: 'space-1',
  siteId: 'site-1',
  name: 'Conference Room A',
  code: 'CR-A',
  description: 'Main conference room',
  isPhysical: true,
  geometry: {
    type: 'rectangle',
    coordinates: [
      { x: 100, y: 100 },
      { x: 300, y: 100 },
      { x: 300, y: 250 },
      { x: 100, y: 250 },
    ],
  },
  capacity: 1,
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-01-01T00:00:00Z',
};

describe('useSpaces', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('useSpaces query', () => {
    it('fetches spaces for a site', async () => {
      const spaces = [mockSpace];
      vi.mocked(spaceApi.getSpaces).mockResolvedValue(spaces);

      const { result } = renderHook(() => useSpaces('site-1'), {
        wrapper: createTestQueryWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(spaces);
      expect(spaceApi.getSpaces).toHaveBeenCalledWith('site-1');
    });

    it('does not fetch when siteId is null', () => {
      const { result } = renderHook(() => useSpaces(null), {
        wrapper: createTestQueryWrapper(),
      });

      expect(result.current.data).toBeUndefined();
      expect(spaceApi.getSpaces).not.toHaveBeenCalled();
    });

    it('handles fetch errors', async () => {
      vi.mocked(spaceApi.getSpaces).mockRejectedValue(new Error('Network error'));

      const { result } = renderHook(() => useSpaces('site-1'), {
        wrapper: createTestQueryWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(result.current.error).toEqual(new Error('Network error'));
    });
  });

  describe('useCreateSpace', () => {
    it('creates a space and invalidates cache', async () => {
      const newSpace = { ...mockSpace, id: 'space-2' };
      vi.mocked(spaceApi.createSpace).mockResolvedValue(newSpace);
      vi.mocked(spaceApi.getSpaces)
        .mockResolvedValueOnce([mockSpace])  // Initial fetch
        .mockResolvedValueOnce([mockSpace, newSpace]);  // After create

      const wrapper = createTestQueryWrapper();
      const { result: createResult } = renderHook(() => useCreateSpace('site-1'), { wrapper });
      const { result: queryResult } = renderHook(() => useSpaces('site-1'), { wrapper });

      await waitFor(() => expect(queryResult.current.isSuccess).toBe(true));
      expect(queryResult.current.data).toHaveLength(1);

      await createResult.current.mutateAsync({
        name: 'Conference Room B',
        code: 'CR-B',
        isPhysical: true,
      });

      await waitFor(() => expect(queryResult.current.data).toHaveLength(2));
      expect(spaceApi.createSpace).toHaveBeenCalledWith('site-1', {
        name: 'Conference Room B',
        code: 'CR-B',
        isPhysical: true,
      });
    });

    it('handles create errors', async () => {
      vi.mocked(spaceApi.createSpace).mockRejectedValue(new Error('Duplicate code'));

      const { result } = renderHook(() => useCreateSpace('site-1'), {
        wrapper: createTestQueryWrapper(),
      });

      await expect(
        result.current.mutateAsync({
          name: 'Test',
          code: 'TEST',
          isPhysical: true,
        })
      ).rejects.toThrow('Duplicate code');
    });
  });

  describe('useUpdateSpace', () => {
    it('updates a space and invalidates cache', async () => {
      const updatedSpace = { ...mockSpace, name: 'Updated Name' };
      vi.mocked(spaceApi.updateSpace).mockResolvedValue(updatedSpace);
      vi.mocked(spaceApi.getSpaces).mockResolvedValueOnce([mockSpace])
        .mockResolvedValueOnce([updatedSpace]);

      const wrapper = createTestQueryWrapper();
      const { result: updateResult } = renderHook(() => useUpdateSpace('site-1'), { wrapper });
      const { result: queryResult } = renderHook(() => useSpaces('site-1'), { wrapper });

      await waitFor(() => expect(queryResult.current.isSuccess).toBe(true));
      expect(queryResult.current.data?.[0].name).toBe('Conference Room A');

      await updateResult.current.mutateAsync({
        spaceId: 'space-1',
        data: {
          name: 'Updated Name',
          code: 'CR-A',
          isPhysical: true,
        },
      });

      await waitFor(() => expect(queryResult.current.data?.[0].name).toBe('Updated Name'));
    });
  });

  describe('useDeleteSpace', () => {
    it('deletes a space and invalidates cache', async () => {
      vi.mocked(spaceApi.deleteSpace).mockResolvedValue();
      vi.mocked(spaceApi.getSpaces).mockResolvedValueOnce([mockSpace])
        .mockResolvedValueOnce([]);

      const wrapper = createTestQueryWrapper();
      const { result: deleteResult } = renderHook(() => useDeleteSpace('site-1'), { wrapper });
      const { result: queryResult } = renderHook(() => useSpaces('site-1'), { wrapper });

      await waitFor(() => expect(queryResult.current.isSuccess).toBe(true));
      expect(queryResult.current.data).toHaveLength(1);

      await deleteResult.current.mutateAsync('space-1');

      await waitFor(() => expect(queryResult.current.data).toHaveLength(0));
      expect(spaceApi.deleteSpace).toHaveBeenCalledWith('site-1', 'space-1');
    });
  });

  describe('useMoveSpace', () => {
    it('moves a space by updating geometry', async () => {
      const newGeometry: SpaceGeometry = {
        type: 'rectangle',
        coordinates: [
          { x: 300, y: 300 },
          { x: 500, y: 300 },
          { x: 500, y: 450 },
          { x: 300, y: 450 },
        ],
      };
      const movedSpace = { ...mockSpace, geometry: newGeometry };
      vi.mocked(spaceApi.updateSpace).mockResolvedValue(movedSpace);

      const { result } = renderHook(() => useMoveSpace('site-1'), {
        wrapper: createTestQueryWrapper(),
      });

      await result.current.mutateAsync({
        spaceId: 'space-1',
        space: mockSpace,
        newGeometry,
      });

      expect(spaceApi.updateSpace).toHaveBeenCalledWith('site-1', 'space-1', {
        name: mockSpace.name,
        code: mockSpace.code,
        description: mockSpace.description,
        isPhysical: mockSpace.isPhysical,
        geometry: newGeometry,
      });
    });
  });

  describe('useMoveSpace — resize operation', () => {
    it('resizes a space by updating geometry', async () => {
      const newGeometry: SpaceGeometry = {
        type: 'rectangle',
        coordinates: [
          { x: 100, y: 100 },
          { x: 500, y: 100 },
          { x: 500, y: 400 },
          { x: 100, y: 400 },
        ],
      };
      const resizedSpace = { ...mockSpace, geometry: newGeometry };
      vi.mocked(spaceApi.updateSpace).mockResolvedValue(resizedSpace);

      const { result } = renderHook(() => useMoveSpace('site-1'), {
        wrapper: createTestQueryWrapper(),
      });

      await result.current.mutateAsync({
        spaceId: 'space-1',
        space: mockSpace,
        newGeometry,
      });

      expect(spaceApi.updateSpace).toHaveBeenCalledWith('site-1', 'space-1', {
        name: mockSpace.name,
        code: mockSpace.code,
        description: mockSpace.description,
        isPhysical: mockSpace.isPhysical,
        geometry: newGeometry,
      });
    });
  });

  describe('cache invalidation', () => {
    it('invalidates both spaces and requests queries after mutation', async () => {
      const queryClient = new QueryClient({
        defaultOptions: {
          queries: { retry: false },
          mutations: { retry: false },
        },
      });
      
      const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
      
      const wrapper = ({ children }: { children: React.ReactNode }) => (
        <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
      );

      vi.mocked(spaceApi.createSpace).mockResolvedValue(mockSpace);

      const { result } = renderHook(() => useCreateSpace('site-1'), { wrapper });

      await result.current.mutateAsync({
        name: 'Test',
        code: 'TEST',
        isPhysical: true,
      });

      await waitFor(() => {
        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['spaces', 'site-1'] });
        expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['requests'] });
      });
    });
  });
});
