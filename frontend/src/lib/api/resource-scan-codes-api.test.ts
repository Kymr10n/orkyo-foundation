import { beforeEach, describe, expect, it, vi } from 'vitest';
import * as apiClient from '../core/api-client';
import {
  getResourceScanCodes,
  linkResourceScanCode,
  lookupScanCode,
  unlinkResourceScanCode,
} from './resource-scan-codes-api';
import { getResourceStatus } from './resource-status-api';

vi.mock('../core/api-client');

describe('resource-scan-codes-api', () => {
  beforeEach(() => vi.clearAllMocks());

  it('looks a code up through a query parameter, since a code can hold "/" and "?"', async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue({ status: 'unknown' });

    await lookupScanCode('https://vendor.example/a?b=1#c');

    expect(apiClient.apiGet).toHaveBeenCalledWith('/api/resources/scan-codes/lookup', {
      params: { code: 'https://vendor.example/a?b=1#c' },
    });
  });

  it('lists, links, moves and unlinks the codes of one resource', async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue([]);
    vi.mocked(apiClient.apiPost).mockResolvedValue({});

    await getResourceScanCodes('r1');
    await linkResourceScanCode('r1', 'A');
    await linkResourceScanCode('r1', 'B', true);
    await unlinkResourceScanCode('r1', 'c1');

    expect(apiClient.apiGet).toHaveBeenCalledWith('/api/resources/r1/scan-codes');
    expect(apiClient.apiPost).toHaveBeenCalledWith('/api/resources/r1/scan-codes', { code: 'A', moveFromOtherResource: false });
    expect(apiClient.apiPost).toHaveBeenCalledWith('/api/resources/r1/scan-codes', { code: 'B', moveFromOtherResource: true });
    expect(apiClient.apiDelete).toHaveBeenCalledWith('/api/resources/r1/scan-codes/c1');
  });

  it('reads the status of one resource', async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue({ resourceId: 'r1' });

    await getResourceStatus('r1');

    expect(apiClient.apiGet).toHaveBeenCalledWith('/api/resources/r1/status');
  });
});
