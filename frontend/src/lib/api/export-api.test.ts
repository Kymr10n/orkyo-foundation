import { describe, it, expect, vi, beforeEach } from 'vitest';
import { exportTenantData } from './export-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

describe('export-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('exportTenantData calls apiPost with default empty request', async () => {
    const mockPayload = { schemaVersion: '1.0', provenance: {}, data: {} };
    vi.mocked(apiClient.apiPost).mockResolvedValue(mockPayload);
    const result = await exportTenantData();
    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.ADMIN.EXPORT, {});
    expect(result.schemaVersion).toBe('1.0');
  });

  it('exportTenantData passes through request options', async () => {
    vi.mocked(apiClient.apiPost).mockResolvedValue({ schemaVersion: '1.0' });
    const req = { siteIds: ['s1'], includeMasterData: true, includePlanningData: false };
    await exportTenantData(req);
    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.ADMIN.EXPORT, req);
  });
});
