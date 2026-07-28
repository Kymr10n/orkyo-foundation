import { describe, it, expect, vi, beforeEach } from 'vitest';
import { getGroupCapabilities, addGroupCapability, deleteGroupCapability } from './group-capability-api';
import * as apiClient from '../core/api-client';

vi.mock('../core/api-client');

/**
 * URLs are asserted as LITERALS, not as `API_PATHS.groupCapabilities('g1')`.
 *
 * The original tests compared the call argument to the same expression the source uses, which is a
 * tautology — it passes for any value of the constant. That is precisely why these tests stayed
 * green for ten weeks while every call 404'd against the renamed backend route. Literals also catch
 * the one gap the backend ApiPathContractTests cannot see: this module reaching for the *wrong*
 * constant. See reporting-tokens-api.test.ts for the same convention.
 */
describe('group-capability-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('getGroupCapabilities GETs the tenant resource-group capabilities route', async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue([]);
    await getGroupCapabilities('g1');
    expect(apiClient.apiGet).toHaveBeenCalledWith('/api/resource-groups/g1/capabilities');
  });

  it('addGroupCapability POSTs to the same collection route', async () => {
    const req = { criterionId: 'c1', value: 42 };
    vi.mocked(apiClient.apiPost).mockResolvedValue({ id: 'cap-1', ...req });
    await addGroupCapability('g1', req);
    expect(apiClient.apiPost).toHaveBeenCalledWith('/api/resource-groups/g1/capabilities', req);
  });

  it('deleteGroupCapability DELETEs the individual capability route', async () => {
    vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);
    await deleteGroupCapability('g1', 'cap-1');
    expect(apiClient.apiDelete).toHaveBeenCalledWith('/api/resource-groups/g1/capabilities/cap-1');
  });
});
