import { describe, it, expect, vi, beforeEach } from 'vitest';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import { getRequestPlan, getSitePlan } from './request-plan-api';

vi.mock('../core/api-client');

beforeEach(() => {
  vi.clearAllMocks();
  vi.mocked(apiClient.apiGet).mockResolvedValue({ groups: [], children: [], edges: [] });
});

describe('getRequestPlan', () => {
  it('reads one parent’s plan from its own endpoint', async () => {
    await getRequestPlan('req-1');
    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.requestPlan('req-1'));
  });
});

describe('getSitePlan', () => {
  it('scopes the plan to a site when one is selected', async () => {
    await getSitePlan('site-1');
    expect(apiClient.apiGet).toHaveBeenCalledWith(`${API_PATHS.sitePlan}?siteId=site-1`);
  });

  it('asks for the whole tenant when no site is selected', async () => {
    // "All sites" is null, not a missing argument — the query param must be absent rather
    // than sent as the string "null", which the backend would reject as a malformed guid.
    await getSitePlan(null);
    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.sitePlan);
  });

  it('asks for the whole tenant when the argument is omitted', async () => {
    await getSitePlan();
    expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.sitePlan);
  });
});
