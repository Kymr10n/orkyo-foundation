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
