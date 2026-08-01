/**
 * Structured error codes returned in the body of 4xx responses.
 *
 * The frontend switches behaviour on these codes (redirect vs toast, etc.).
 * Keep values in sync with `backend/shared/ApiErrorCodes.cs`.
 */
export const API_ERROR_CODES = {
  /** BFF session has ended or is otherwise not authenticated. Redirect to login. */
  SESSION_EXPIRED: 'session_expired',
  /** Site-admin's break-glass session for this tenant has expired or was revoked. Exit tenant. */
  BREAK_GLASS_EXPIRED: 'break_glass_expired',
  /** Break-glass session hit the absolute hard cap and cannot be renewed. Exit tenant. */
  BREAK_GLASS_HARD_CAP_REACHED: 'break_glass_hard_cap_reached',
  /** Generic permission denial — user is authenticated but not allowed. */
  FORBIDDEN: 'forbidden',
  /** Self-service account change blocked for a shared/locked identity (e.g. the public demo account). */
  ACCOUNT_LOCKED: 'account_locked',
  /** Tier/plan quota for a resource (sites, spaces, seats) has been reached. */
  QUOTA_EXCEEDED: 'quota_exceeded',
} as const;

/**
 * The single error body shape the application API returns: RFC 7807 ProblemDetails plus the
 * machine-readable extensions the frontend switches on. Mirrors the backend's
 * `OrkyoProblemDetails` (backend/src/Helpers/OrkyoProblemDetails.cs).
 *
 * Consolidated from five coexisting shapes (#96). Note `/api/reporting/v1` is deliberately
 * excluded — it is a versioned contract for external consumers and keeps its `{error, message}`
 * body; nothing in this frontend calls it.
 */
export interface ApiErrorBody {
  /** URI reference identifying the problem type, derived from `code`. */
  type?: string;
  /** Short, stable human-readable summary (e.g. "Forbidden"). */
  title?: string;
  /** Human-readable explanation of this specific occurrence — the message to surface. */
  detail?: string;
  /** HTTP status, repeated in the body per RFC 7807. */
  status?: number;
  /** Machine-readable error code; the field behaviour switches on. */
  code?: string;
  /** Relative path to navigate to after handling (e.g. "/site-admin" when break-glass ends). */
  returnTo?: string;
  /** Resource type the error refers to, e.g. "spaces" on a quota_exceeded response. */
  resourceType?: string;
  /** Numeric limit associated with the error, e.g. the tier max on quota_exceeded. */
  limit?: number;
  /** Per-field validation messages, present only on validation failures. */
  errors?: Record<string, string[]>;
}
