import type { RequestStatus } from "@foundation/src/types/requests";

/**
 * Request status wire values, as stored in `requests.status`.
 * Mirrors the backend `RequestStatus` enum ([JsonStringEnumMemberName]) and the
 * `RequestStatus` union in types/requests.ts (which remains the type source).
 */
export const REQUEST_STATUS = {
  NEW: "new",
  IN_PROGRESS: "in_progress",
  DONE: "done",
  CANCELLED: "cancelled",
  DEFERRED: "deferred",
} as const satisfies Record<string, RequestStatus>;

/**
 * Canonical display order for request statuses: active first, then terminal/hold.
 * Single source of truth for status-filter dropdowns and legends so they never drift.
 */
export const REQUEST_STATUS_ORDER: RequestStatus[] = [
  REQUEST_STATUS.NEW,
  REQUEST_STATUS.IN_PROGRESS,
  REQUEST_STATUS.DONE,
  REQUEST_STATUS.DEFERRED,
  REQUEST_STATUS.CANCELLED,
];
