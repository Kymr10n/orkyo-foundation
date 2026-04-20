/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import {
  useSpaceGroups,
  useCreateSpaceGroup,
  useUpdateSpaceGroup,
  useDeleteSpaceGroup
} from './useGroups';
import * as groupsApi from '@/lib/api/space-groups-api';
import type { SpaceGroup } from '@/types/spaceGroup';
import { createTestQueryWrapper, createTestQueryClientWithSpy } from '@/test-utils';

vi.mock('@/lib/api/space-groups-api');

const mockGroup: SpaceGroup = {
  id: 'group-1',
  name: 'Conference Rooms',
  description: 'All conference rooms',
  color: '#3b82f6',
  displayOrder: 0,
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-01-01T00:00:00Z',
};

describe('useGroups', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('useSpaceGroups query', () => {
    it('fetches space groups for a site', async () => {
      const groups = [mockGroup];
      vi.mocked(groupsApi.getSpaceGroups).mockResolvedValue(groups);

      const { result } = renderHook(() => useSpaceGroups('site-1'), {
        wrapper: createTestQueryWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(groups);
    });

    it('does not fetch when siteId is null', () => {
      const { result } = renderHook(() => useSpaceGroups(null), {
        wrapper: createTestQueryWrapper(),
      });

      expect(result.current.data).toBeUndefined();
      expect(groupsApi.getSpaceGroups).not.toHaveBeenCalled();
    });
  });

  describe('useCreateSpaceGroup', () => {
    it('creates a group and invalidates related caches', async () => {
      const { spy, wrapper } = createTestQueryClientWithSpy();

      vi.mocked(groupsApi.createSpaceGroup).mockResolvedValue(mockGroup);

      const { result } = renderHook(() => useCreateSpaceGroup('site-1'), { wrapper });

      await result.current.mutateAsync({
        name: 'Test Group',
        description: 'Test',
      });

      await waitFor(() => {
        expect(spy).toHaveBeenCalledWith({ queryKey: ['space-groups', 'site-1'] });
        expect(spy).toHaveBeenCalledWith({ queryKey: ['spaces', 'site-1'] });
        expect(spy).toHaveBeenCalledWith({ queryKey: ['requests'] });
      });
    });
  });

  describe('useUpdateSpaceGroup', () => {
    it('updates a group and invalidates caches', async () => {
      const updatedGroup = { ...mockGroup, name: 'Updated Name' };
      vi.mocked(groupsApi.updateSpaceGroup).mockResolvedValue(updatedGroup);

      const { result } = renderHook(() => useUpdateSpaceGroup('site-1'), {
        wrapper: createTestQueryWrapper(),
      });

      await result.current.mutateAsync({
        id: 'group-1',
        data: {
          name: 'Updated Name',
          description: 'Test',
        },
      });

      expect(groupsApi.updateSpaceGroup).toHaveBeenCalledWith('group-1', expect.any(Object));
    });
  });

  describe('useDeleteSpaceGroup', () => {
    it('deletes a group and invalidates caches', async () => {
      vi.mocked(groupsApi.deleteSpaceGroup).mockResolvedValue();

      const { result } = renderHook(() => useDeleteSpaceGroup('site-1'), {
        wrapper: createTestQueryWrapper(),
      });

      await result.current.mutateAsync('group-1');

      expect(groupsApi.deleteSpaceGroup).toHaveBeenCalledWith('group-1');
    });
  });
});
