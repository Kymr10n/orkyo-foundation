/**
 * CSRF double-submit cookie utilities for BFF auth.
 *
 * The BFF callback sets an `orkyo-csrf` cookie (NOT HttpOnly) alongside
 * the HttpOnly session cookie. For mutating requests (POST/PUT/PATCH/DELETE),
 * the backend expects an `X-CSRF-Token` header whose value matches the cookie.
 */

const CSRF_COOKIE_NAME = 'orkyo-csrf';
export const CSRF_HEADER_NAME = 'X-CSRF-Token';

/**
 * Read the current CSRF token from cookies.
 * Returns null if no token is set (user not authenticated via BFF).
 */
export function getCsrfToken(): string | null {
  const match = new RegExp(`(?:^|;\\s*)${CSRF_COOKIE_NAME}=([^;]+)`).exec(document.cookie);
  return match?.[1] ?? null;
}

/**
 * HTTP methods that require CSRF protection.
 */
const MUTATING_METHODS = new Set(['POST', 'PUT', 'PATCH', 'DELETE']);

/**
 * Returns true if this HTTP method requires a CSRF token header.
 */
export function isMutatingMethod(method: string): boolean {
  return MUTATING_METHODS.has(method.toUpperCase());
}
