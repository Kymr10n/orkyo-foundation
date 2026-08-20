import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('../core/api-client', () => ({
  apiGet: vi.fn(),
  apiPost: vi.fn(),
  apiRawFetch: vi.fn(),
}));

import { apiGet, apiPost, apiRawFetch } from '../core/api-client';
import {
  activateCatalogType,
  deactivateCatalogType,
  getResourceTypeCatalog,
  purgeCatalogType,
} from './resource-type-catalog-api';

describe('resource-type-catalog-api', () => {
  beforeEach(() => vi.clearAllMocks());

  it('lists the catalog', async () => {
    vi.mocked(apiGet).mockResolvedValue([]);

    await getResourceTypeCatalog();

    expect(apiGet).toHaveBeenCalledWith('/api/resource-type-catalog');
  });

  it('activates by key', async () => {
    vi.mocked(apiPost).mockResolvedValue({});

    await activateCatalogType('drill');

    expect(apiPost).toHaveBeenCalledWith('/api/resource-type-catalog/drill/activate', undefined);
  });

  it('deactivates by key without parsing the empty body', async () => {
    vi.mocked(apiPost).mockResolvedValue(undefined);

    await deactivateCatalogType('drill');

    expect(apiPost).toHaveBeenCalledWith('/api/resource-type-catalog/drill/deactivate', undefined, {
      skipJsonParse: true,
    });
  });

  it('purges by key and returns the counts from the DELETE body', async () => {
    const counts = { resources: 2, assignments: 3, groups: 1, requestTargets: 0 };
    vi.mocked(apiRawFetch).mockResolvedValue({ json: () => Promise.resolve(counts) } as Response);

    const result = await purgeCatalogType('drill');

    expect(apiRawFetch).toHaveBeenCalledWith('/api/resource-type-catalog/drill', 'DELETE');
    expect(result).toEqual(counts);
  });
});
