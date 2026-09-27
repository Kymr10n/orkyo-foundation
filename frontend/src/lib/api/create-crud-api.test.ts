import { describe, it, expect, vi, beforeEach } from 'vitest';
import { createCrudApi } from './create-crud-api';
import * as apiUtils from '../core/api-utils';

vi.mock('../core/api-utils');

describe('createCrudApi list', () => {
  const api = createCrudApi<unknown, unknown, unknown>({
    collectionPath: '/api/things',
    itemPath: (id) => `/api/things/${id}`,
  });

  beforeEach(() => {
    vi.mocked(apiUtils.getApiHeaders).mockReturnValue({});
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [] } as Response);
  });

  function fetchedUrl(): URL {
    return new URL(vi.mocked(fetch).mock.calls[0][0] as string);
  }

  it('URL-encodes query values, so a value cannot add or split parameters', async () => {
    await api.list({ resourceType: 'a&b=c', note: 'x y' });

    expect(fetchedUrl().searchParams.get('resourceType')).toBe('a&b=c');
    expect(fetchedUrl().searchParams.get('note')).toBe('x y');
    expect([...fetchedUrl().searchParams.keys()]).toEqual(['resourceType', 'note']);
  });

  it('asks for the bare collection without a query', async () => {
    await api.list();

    expect(fetchedUrl().pathname).toBe('/api/things');
    expect(fetchedUrl().search).toBe('');
  });
});
