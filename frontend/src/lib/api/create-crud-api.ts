/**
 * Small factory for the get-list / get / create / update / remove shape shared by
 * the trivially-CRUD reference-data clients (departments, job titles, …).
 *
 * Only the pure-CRUD surface lives here. Genuinely custom endpoints (tree views,
 * member replace-all, applicability, composite paths) stay as explicit functions
 * in their own modules.
 *
 * The list query string is built by appending `?key=value` pairs to the collection
 * path (rather than going through apiGet's `params` option) so the produced request
 * is byte-identical to the hand-written clients this replaces.
 */

import { apiGet, apiPost, apiPut, apiDelete } from '../core/api-client';

export interface CrudApi<TEntity, TCreate, TUpdate> {
  /** GET the collection. Optional query params are appended as `?k=v&…`. */
  list(query?: Record<string, string>): Promise<TEntity[]>;
  /** GET a single item by id. */
  get(id: string): Promise<TEntity>;
  /** POST a new item to the collection. */
  create(request: TCreate): Promise<TEntity>;
  /** PUT an update to an item by id. */
  update(id: string, request: TUpdate): Promise<TEntity>;
  /** DELETE an item by id. */
  remove(id: string): Promise<void>;
}

interface CrudApiConfig {
  /** Collection path, e.g. '/api/departments'. */
  collectionPath: string;
  /** Builds the item path from an id, e.g. (id) => `/api/departments/${id}`. */
  itemPath: (id: string) => string;
}

function buildQuery(query?: Record<string, string>): string {
  if (!query) return '';
  const entries = Object.entries(query);
  if (entries.length === 0) return '';
  return '?' + entries.map(([k, v]) => `${k}=${v}`).join('&');
}

export function createCrudApi<TEntity, TCreate, TUpdate>(
  config: CrudApiConfig,
): CrudApi<TEntity, TCreate, TUpdate> {
  return {
    list: (query) => apiGet<TEntity[]>(`${config.collectionPath}${buildQuery(query)}`),
    get: (id) => apiGet<TEntity>(config.itemPath(id)),
    create: (request) => apiPost<TEntity>(config.collectionPath, request),
    update: (id, request) => apiPut<TEntity>(config.itemPath(id), request),
    remove: (id) => apiDelete(config.itemPath(id)),
  };
}
