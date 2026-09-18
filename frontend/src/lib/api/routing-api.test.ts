import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  getRoutings,
  createRouting,
  updateRouting,
  deleteRouting,
  instantiateRouting,
} from './routing-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

const routing = { id: 'r1', name: 'Bracket', steps: [] };
const step = { stepNo: 1, operationTemplateId: 't1', setupMinutes: 10, runMinutesPerUnit: 5, lagMinutesAfter: 0 };

describe('routing-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('getRoutings reads the collection', async () => {
    vi.mocked(apiClient.apiGet).mockResolvedValue([routing]);
    expect(await getRoutings()).toEqual([routing]);
    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.ROUTINGS);
  });

  it('createRouting posts to the collection', async () => {
    vi.mocked(apiClient.apiPost).mockResolvedValue(routing);
    const req = { name: 'Bracket', steps: [step] };
    expect(await createRouting(req)).toEqual(routing);
    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.ROUTINGS, req);
  });

  it('updateRouting puts to the item', async () => {
    vi.mocked(apiClient.apiPut).mockResolvedValue(routing);
    const req = { name: 'Bracket v2', steps: [step] };
    await updateRouting('r1', req);
    expect(apiClient.apiPut).toHaveBeenCalledWith(API_PATHS.routing('r1'), req);
  });

  it('deleteRouting deletes the item', async () => {
    vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);
    await deleteRouting('r1');
    expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.routing('r1'));
  });

  it('instantiateRouting posts the work order to the instantiate path', async () => {
    const response = { parent: { id: 'p1', name: 'WO-1' }, childIds: ['c1'] };
    vi.mocked(apiClient.apiPost).mockResolvedValue(response);
    const req = { name: 'WO-1', quantity: 4 };
    expect(await instantiateRouting('r1', req)).toEqual(response);
    expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.routingInstantiate('r1'), req);
  });
});
