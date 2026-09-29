import { describe, it, expect, vi } from 'vitest';
import { createCrudApi } from './create-crud-api';
import * as apiClient from '../core/api-client';

vi.mock('../core/api-client');

// Query encoding belongs to apiGet and is covered in api-client.test.ts; this pins the paths
// and the verbs the factory maps each operation onto.
describe('createCrudApi', () => {
  const api = createCrudApi<unknown, unknown, unknown>({
    collectionPath: '/api/things',
    itemPath: (id) => `/api/things/${id}`,
  });

  it('lists the collection with the query as params', async () => {
    await api.list({ resourceType: 'a&b=c' });
    expect(apiClient.apiGet).toHaveBeenCalledWith('/api/things', { params: { resourceType: 'a&b=c' } });
  });

  it('asks for the bare collection without a query', async () => {
    await api.list();
    expect(apiClient.apiGet).toHaveBeenCalledWith('/api/things', { params: undefined });
  });

  it('maps get, create, update and remove onto the item and collection paths', async () => {
    await api.get('t1');
    await api.create({ name: 'x' });
    await api.update('t1', { name: 'y' });
    await api.remove('t1');

    expect(apiClient.apiGet).toHaveBeenCalledWith('/api/things/t1');
    expect(apiClient.apiPost).toHaveBeenCalledWith('/api/things', { name: 'x' });
    expect(apiClient.apiPut).toHaveBeenCalledWith('/api/things/t1', { name: 'y' });
    expect(apiClient.apiDelete).toHaveBeenCalledWith('/api/things/t1');
  });
});
