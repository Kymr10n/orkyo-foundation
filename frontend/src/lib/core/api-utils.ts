/**
 * Shared API utilities for making authenticated requests.
 *
 * BFF mode: authentication is carried by HttpOnly cookies (credentials: 'include').
 * No Bearer token is sent from the frontend.
 */

import { runtimeConfig } from "@foundation/src/config/runtime";
import { API_ERROR_CODES, API_ERROR_MESSAGES, type ApiErrorBody } from "@foundation/src/constants/api-error-codes";
import { CORRELATION_ID_HEADER_NAME, TENANT_HEADER_NAME } from "@foundation/src/constants/http";
import { tenantStorage } from "@foundation/src/lib/core/tenant-storage";
import { ROUTE_SITE_ADMIN } from "@foundation/src/constants/auth";
import { getCsrfToken, CSRF_HEADER_NAME, isMutatingMethod } from "@foundation/src/lib/core/csrf";
import { logger } from "@foundation/src/lib/core/logger";
import { randomId } from "@foundation/src/lib/core/ids";
import {
  extractSlugFromHostname,
  isSafeRelativePath,
  goToApex,
  redirectToLogin,
} from "@foundation/src/lib/utils/tenant-navigation";
import { sessionEndRedirect } from "@foundation/src/lib/utils/session-end";

/**
 * Get common headers for API requests.
 *
 * BFF mode: no Authorization header — auth is via HttpOnly cookie.
 * Pass `method` to automatically include the CSRF token for mutating requests.
 */
export function getApiHeaders(method = 'GET'): Record<string, string> {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    [CORRELATION_ID_HEADER_NAME]: randomId(),
  };

  // Add tenant identifier
  const tenantSlug = getTenantSlug();
  if (tenantSlug) {
    headers[TENANT_HEADER_NAME] = tenantSlug;
  }
  // No fallback — if tenant slug is missing, let the backend reject with a clear 400

  // Add CSRF token for mutating requests (BFF cookie auth)
  if (isMutatingMethod(method)) {
    const csrf = getCsrfToken();
    if (csrf) {
      headers[CSRF_HEADER_NAME] = csrf;
    }
  }

  return headers;
}

/**
 * Get tenant slug from URL subdomain or, without one, the remembered slug
 */
export function getTenantSlug(): string {
  const slug = extractSlugFromHostname(window.location.hostname);
  if (slug) {
    logger.debug("getTenantSlug() from subdomain:", slug);
    return slug;
  }

  // For local development or single-tenant deployment, use stored tenant
  const stored = tenantStorage.slug();
  logger.debug("getTenantSlug() from storage:", stored);
  return stored;
}

/**
 * Base API URL - re-exported from runtime config for backwards compatibility
 */
export const API_BASE_URL = runtimeConfig.apiBaseUrl;


/**
 * An error response from the application API. `status` is the HTTP status and `code` the
 * body's machine-readable code, so a caller switches on those instead of matching `message`,
 * which is the human text meant for the user.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly code?: string;

  constructor(message: string, status: number, code?: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
  }
}

/**
 * Join RFC 7807 `errors` into one readable sentence, or null when the body carries none.
 * Without this a validation failure surfaces only the generic problem `detail`, which tells
 * the user nothing about which field they got wrong.
 */
function flattenFieldErrors(body: ApiErrorBody): string | null {
  const messages = Object.values(body.errors ?? {}).flat().filter(Boolean);
  return messages.length > 0 ? messages.join(" ") : null;
}

/**
 * Handle API errors consistently.
 *
 * The backend returns one canonical body for every error — RFC 7807 ProblemDetails plus a
 * machine-readable `code` (and `returnTo` where relevant) — so the frontend can react
 * differently per case rather than treating any 401/403 as "session expired":
 *
 *   - `break_glass_expired`, `break_glass_hard_cap_reached` → clear the tenant slug, go to the
 *     server's `returnTo` (same-origin only) or apex /site-admin
 *   - any other 401 (`session_expired`)     → clear the tenant slug, go to apex /login (or,
 *     for an ephemeral demo session, to where it ends)
 *   - anything else                         → throw `ApiError`; the caller surfaces it
 *
 * Every branch throws `ApiError { status, code }` with the problem's message.
 *
 * Returning to /site-admin instead of /login matters: a site-admin whose break-glass
 * just timed out should land back on the admin console, not be sent through the
 * login flow as if their identity itself was invalid.
 */
export async function handleApiError(response: Response): Promise<never> {
  let errorMessage = response.statusText;
  let body: ApiErrorBody | null = null;

  try {
    body = await response.json() as ApiErrorBody;
    // RFC 7807: `detail` explains this occurrence, `title` is the generic summary.
    // Field-level validation messages are flattened in so a 400 says what was wrong
    // rather than the useless generic "One or more fields failed validation."
    // A code with its own user-facing text wins over the server's wording.
    errorMessage =
      (body.code ? API_ERROR_MESSAGES[body.code] : undefined) ??
      flattenFieldErrors(body) ?? body.detail ?? body.title ?? errorMessage;
  } catch {
    // Response might not be JSON
  }

  const code = body?.code;
  // The server names where to go next; only a same-origin path is honoured.
  const returnTo = body?.returnTo && isSafeRelativePath(body.returnTo) ? body.returnTo : undefined;

  // Break-glass: route the admin back to /site-admin instead of /login.
  if (
    code === API_ERROR_CODES.BREAK_GLASS_EXPIRED ||
    code === API_ERROR_CODES.BREAK_GLASS_HARD_CAP_REACHED
  ) {
    tenantStorage.clear();
    goToApex(returnTo || ROUTE_SITE_ADMIN);
    throw new ApiError(errorMessage || "Break-glass session has ended.", response.status, code);
  }

  if (response.status === 401) {
    // Session expired or unauthenticated — clear state and redirect.
    tenantStorage.clear();
    // An ephemeral session (the public demo) ends on the marketing site, not at a credentials
    // form its visitor never had. Same shape as the break-glass branch above. Concurrent 401s
    // all read the same answer, so no later one can override this navigation with the login flow.
    const sessionEnd = sessionEndRedirect();
    if (sessionEnd) {
      window.location.replace(sessionEnd);
    } else {
      redirectToLogin();
    }
    throw new ApiError(
      errorMessage || "Your session has expired. Please log in again.",
      response.status,
      code,
    );
  }

  throw new ApiError(errorMessage || `Request failed (${response.status})`, response.status, code);
}
