import type { RequestStatus } from "@foundation/src/types/requests";

/**
 * Request status wire values, as stored in `requests.status`.
 * Mirrors the backend `RequestStatus` enum ([JsonStringEnumMemberName]) and the
 * `RequestStatus` union in types/requests.ts (which remains the type source).
 */
export const REQUEST_STATUS = {
  PLANNED: "planned",
  IN_PROGRESS: "in_progress",
  DONE: "done",
  CANCELLED: "cancelled",
} as const satisfies Record<string, RequestStatus>;
