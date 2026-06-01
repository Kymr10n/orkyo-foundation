import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  getResourceGroups,
  createResourceGroup,
  updateResourceGroup,
  deleteResourceGroup,
  getResourceGroupMembers,
  setResourceGroupMembers,
} from './resource-groups-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

const mockGroup = {
  id: 'grp-1', name: 'Engineers', resourceTypeKey: 'person',
  defaultAvailabilityPercent: 100, memberCount: 3,
  createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z',
};

describe('resource-groups-api', () => {
  beforeEach(() => vi.clearAllMocks());

  describe('getResourceGroups', () => {
    it('calls apiGet with resourceTypeKey query param (URL-encoded)', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([mockGroup]);
      const result = await getResourceGroups('person');
      expect(apiClient.apiGet).toHaveBeenCalledWith(
        `${API_PATHS.RESOURCE_GROUPS}?resourceTypeKey=person`,
      );
      expect(result).toEqual([mockGroup]);
    });
  });

  describe('createResourceGroup', () => {
    it('calls apiPost with group data', async () => {
      const req = { resourceTypeKey: 'person', name: 'Engineers', defaultAvailabilityPercent: 100 };
      vi.mocked(apiClient.apiPost).mockResolvedValue(mockGroup);
      const result = await createResourceGroup(req);
      expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.RESOURCE_GROUPS, req);
      expect(result).toEqual(mockGroup);
    });
  });

  describe('updateResourceGroup', () => {
    it('calls apiPut on the group endpoint', async () => {
      const req = { name: 'Senior Engineers' };
      vi.mocked(apiClient.apiPut).mockResolvedValue({ ...mockGroup, name: 'Senior Engineers' });
      const result = await updateResourceGroup('grp-1', req);
      expect(apiClient.apiPut).toHaveBeenCalledWith(API_PATHS.resourceGroup('grp-1'), req);
      expect(result.name).toBe('Senior Engineers');
    });
  });

  describe('deleteResourceGroup', () => {
    it('calls apiDelete on the group endpoint', async () => {
      vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);
      await deleteResourceGroup('grp-1');
      expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.resourceGroup('grp-1'));
    });
  });

  describe('getResourceGroupMembers', () => {
    it('calls apiGet on the members endpoint and returns members', async () => {
      const mockResponse = { groupId: 'grp-1', members: [{ id: 'res-1', name: 'Alice' }] };
      vi.mocked(apiClient.apiGet).mockResolvedValue(mockResponse);
      const result = await getResourceGroupMembers('grp-1');
      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.resourceGroupMembers('grp-1'));
      expect(result.members).toHaveLength(1);
    });
  });

  describe('setResourceGroupMembers', () => {
    it('calls apiPut with full replacement member list', async () => {
      const mockResponse = { groupId: 'grp-1', members: [] };
      vi.mocked(apiClient.apiPut).mockResolvedValue(mockResponse);
      const result = await setResourceGroupMembers('grp-1', ['res-1', 'res-2']);
      expect(apiClient.apiPut).toHaveBeenCalledWith(
        API_PATHS.resourceGroupMembers('grp-1'),
        { resourceIds: ['res-1', 'res-2'] },
      );
      expect(result.groupId).toBe('grp-1');
    });
  });
});
