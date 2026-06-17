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

  function calledUrl(): string {
    return vi.mocked(apiClient.apiGet).mock.calls[0][0] as string;
  }

  it('serializes from/to/granularity into the query string', async () => {
    await getUtilizationByResource(FROM, TO, 'day');

    const url = calledUrl();
    expect(url.startsWith('/api/utilization/by-resource?')).toBe(true);
    const params = new URLSearchParams(url.split('?')[1]);
    expect(params.get('from')).toBe(FROM.toISOString());
    expect(params.get('to')).toBe(TO.toISOString());
    expect(params.get('granularity')).toBe('day');
    expect(params.has('resourceTypeKey')).toBe(false);
    expect(params.has('siteId')).toBe(false);
  });

  it('includes resourceTypeKey and siteId when provided', async () => {
    await getUtilizationByResource(FROM, TO, 'week', 'person', 'site-a');

    const params = new URLSearchParams(calledUrl().split('?')[1]);
    expect(params.get('resourceTypeKey')).toBe('person');
    expect(params.get('siteId')).toBe('site-a');
  });

  it('omits siteId when null or undefined', async () => {
    await getUtilizationByResource(FROM, TO, 'week', 'person', null);
    expect(new URLSearchParams(calledUrl().split('?')[1]).has('siteId')).toBe(false);
  });
});
