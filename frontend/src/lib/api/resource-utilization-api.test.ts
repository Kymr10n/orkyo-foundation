import { describe, it, expect, vi, beforeEach } from 'vitest';
import { getUtilizationByResource } from './resource-utilization-api';
import * as apiClient from '../core/api-client';

vi.mock('../core/api-client');

const FROM = new Date('2026-05-01T00:00:00.000Z');
const TO = new Date('2026-05-08T00:00:00.000Z');

describe('resource-utilization-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(apiClient.apiGet).mockResolvedValue([]);
  });

  function calledParams() {
    const [path, options] = vi.mocked(apiClient.apiGet).mock.calls[0];
    expect(path).toBe('/api/utilization/by-resource');
    return options?.params ?? {};
  }

  it('serializes from/to/granularity into the query string', async () => {
    await getUtilizationByResource(FROM, TO, 'day');

    const params = calledParams();
    expect(params.from).toBe(FROM.toISOString());
    expect(params.to).toBe(TO.toISOString());
    expect(params.granularity).toBe('day');
    expect(params.resourceTypeKey).toBeUndefined();
    expect(params.siteId).toBeUndefined();
  });

  it('includes resourceTypeKey and siteId when provided', async () => {
    await getUtilizationByResource(FROM, TO, 'week', 'person', 'site-a');

    const params = calledParams();
    expect(params.resourceTypeKey).toBe('person');
    expect(params.siteId).toBe('site-a');
  });

  it('omits siteId when null or undefined', async () => {
    await getUtilizationByResource(FROM, TO, 'week', 'person', null);
    expect(calledParams().siteId).toBeUndefined();
  });
});
