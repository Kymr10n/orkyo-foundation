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
} as const;

/**
 * Body shape for 401/403/404/410 responses that carry a structured code.
 * The optional `returnTo` is a relative path the frontend should navigate to
 * after handling the error (e.g. "/admin" when a break-glass session ends).
 */
export interface ApiErrorBody {
  error?: string;
  message?: string;
  code?: string;
  returnTo?: string;
}
