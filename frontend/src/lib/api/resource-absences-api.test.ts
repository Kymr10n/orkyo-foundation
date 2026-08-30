import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  getResourceAbsences,
  createResourceAbsence,
  updateResourceAbsence,
  deleteResourceAbsence,
} from './resource-absences-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

const mockAbsence = {
  id: 'abs-1', resourceId: 'res-1', absenceType: 'vacation' as const,
  title: 'Summer vacation', startTs: '2026-07-01T00:00:00Z',
  endTs: '2026-07-14T00:00:00Z', isRecurring: false, enabled: true,
  createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z',
};

describe('resource-absences-api', () => {
  beforeEach(() => vi.clearAllMocks());

  describe('getResourceAbsences', () => {
    it('calls apiGet on the resource absences endpoint', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([mockAbsence]);
      const result = await getResourceAbsences('res-1');
      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.resourceAbsences('res-1'));
      expect(result).toEqual([mockAbsence]);
    });
  });

  describe('createResourceAbsence', () => {
    it('calls apiPost with absence data', async () => {
      const req = { absenceType: 'vacation' as const, title: 'Holiday', startTs: '2026-07-01T00:00:00Z', endTs: '2026-07-07T00:00:00Z' };
      vi.mocked(apiClient.apiPost).mockResolvedValue(mockAbsence);
      const result = await createResourceAbsence('res-1', req);
      expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.resourceAbsences('res-1'), req);
      expect(result).toEqual(mockAbsence);
    });
  });

  describe('deleteResourceAbsence', () => {
    it('calls apiDelete on the specific absence endpoint', async () => {
      vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);
      await deleteResourceAbsence('res-1', 'abs-1');
      expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.resourceAbsence('res-1', 'abs-1'));
    });
  });

  it('updates an absence through the resource-scoped path', async () => {
    vi.mocked(apiClient.apiPut).mockResolvedValue(mockAbsence);

    const result = await updateResourceAbsence('res-1', 'abs-1', {
      absenceType: 'maintenance',
      title: 'Annual service',
      startTs: '2026-07-02T08:00:00Z',
      endTs: '2026-07-02T12:00:00Z',
      enabled: true,
    });

    expect(apiClient.apiPut).toHaveBeenCalledWith(
      API_PATHS.resourceAbsence('res-1', 'abs-1'),
      expect.objectContaining({ absenceType: 'maintenance', title: 'Annual service' }),
    );
    expect(result).toEqual(mockAbsence);
  });
});
