/**
 * The backend's paginated list envelope (`Api.Models.PagedResult<T>`).
 *
 * One shape for every paged list: `items` plus the page metadata. `hasNextPage` is also how a
 * capped unpaged branch says it truncated — it serves the whole list up to a cap and still
 * reports the real `totalItems`, so `hasNextPage` true means rows were left behind.
 */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

/**
 * The pre-0.26.0 list shape, as the two converted endpoints used to answer.
 *
 * `/api/resources` sent `{data, total}` and `/api/admin/feedback` sent `{items, total}`.
 */
interface LegacyListShape<T> {
  data?: T[];
  items?: T[];
  total?: number;
}

/**
 * Accepts either the current envelope or the pre-0.26.0 shape.
 *
 * The products bump the NuGet and npm packages together, but the two land in separate files, so
 * a tree carrying the new client against a backend that still answers the old way is reachable.
 * Without this it fails silently: the list renders empty and `totalItems` is `undefined`, which
 * makes a paging loop's `length >= total` comparison false forever.
 *
 * One function for both endpoints, because a per-endpoint shim is how the two drifted in the
 * first place — resources got one and feedback did not.
 *
 * Remove in the release after 0.26.0, once no supported backend answers the old shape.
 */
export function normalizePagedResult<T>(
  response: PagedResult<T> & LegacyListShape<T>,
  fallbackPageSize: number,
): PagedResult<T> {
  const items = response.items ?? response.data ?? [];
  const totalItems = response.totalItems ?? response.total ?? items.length;
  const pageSize = response.pageSize ?? fallbackPageSize;
  const page = response.page ?? 1;
  return {
    items,
    page,
    pageSize,
    totalItems,
    totalPages: response.totalPages ?? (pageSize > 0 ? Math.ceil(totalItems / pageSize) : 0),
    hasNextPage: response.hasNextPage ?? page * pageSize < totalItems,
    hasPreviousPage: response.hasPreviousPage ?? page > 1,
  };
}
