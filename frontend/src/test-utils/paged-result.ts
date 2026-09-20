import type { PagedResult } from '@foundation/src/lib/core/paged-result';

/**
 * A complete `PagedResult<T>` around the given items — one page holding all of them.
 *
 * The envelope has seven fields and a test that mocks an API client needs every one, so
 * building it by hand in each file is where the shapes drift apart.
 */
export function pagedResult<T>(items: T[], overrides: Partial<PagedResult<T>> = {}): PagedResult<T> {
  const pageSize = overrides.pageSize ?? Math.max(items.length, 1);
  const totalItems = overrides.totalItems ?? items.length;
  const page = overrides.page ?? 1;
  const totalPages = Math.ceil(totalItems / pageSize);
  return {
    items,
    page,
    pageSize,
    totalItems,
    totalPages,
    hasNextPage: page < totalPages,
    hasPreviousPage: page > 1,
    ...overrides,
  };
}
