/**
 * Shared API utilities for making authenticated requests.
 *
 * BFF mode: authentication is carried by HttpOnly cookies (credentials: 'include').
 * No Bearer token is sent from the frontend.
 */

import { runtimeConfig } from "@/config/runtime";
import { API_ERROR_CODES, type ApiErrorBody } from "@/constants/api-error-codes";
import { CORRELATION_ID_HEADER_NAME, TENANT_HEADER_NAME } from "@/constants/http";
import { STORAGE_KEYS } from "@/constants/storage";
import { getTenantSlugSync } from "@/contexts/AuthContext";
import { getCsrfToken, CSRF_HEADER_NAME, isMutatingMethod } from "@/lib/core/csrf";
import { logger } from "@/lib/core/logger";
import { extractSlugFromHostname, navigateToApex, redirectToLogin } from "@/lib/utils/tenant-navigation";

/**
 * Get common headers for API requests.
 *
 * BFF mode: no Authorization header — auth is via HttpOnly cookie.
 * Pass `method` to automatically include the CSRF token for mutating requests.
 */
export function getApiHeaders(method = 'GET'): Record<string, string> {
  const headers: Record<string, string> = {
    "Content-Type": "application/json",
    [CORRELATION_ID_HEADER_NAME]: crypto.randomUUID(),
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
 * Get tenant slug from URL subdomain or localStorage
 */
export function getTenantSlug(): string {
  const slug = extractSlugFromHostname(window.location.hostname);
  if (slug) {
    logger.debug("getTenantSlug() from subdomain:", slug);
    return slug;
  }

  // For local development or single-tenant deployment, use stored tenant
  const stored = getTenantSlugSync() || "";
  if (stored) {
    logger.debug("getTenantSlug() from storage:", stored);
    return stored;
  }

  // Recover from older/local states where only active_membership was persisted.
  try {
    const rawMembership = localStorage.getItem(STORAGE_KEYS.ACTIVE_MEMBERSHIP);
    if (rawMembership) {
      const parsed = JSON.parse(rawMembership) as { slug?: unknown };
      if (typeof parsed.slug === "string" && parsed.slug.length > 0) {
        logger.debug("getTenantSlug() from active membership:", parsed.slug);
        return parsed.slug;
      }
    }
  } catch {
    // Ignore malformed storage data and return empty.
  }

  logger.debug("getTenantSlug() returning empty");
  return "";
}

/**
 * Base API URL - re-exported from runtime config for backwards compatibility
 */
export const API_BASE_URL = runtimeConfig.apiBaseUrl;

/**
 * Get the API base URL
 */
export function getApiUrl(): string {
  return runtimeConfig.apiBaseUrl;
}

/**
 * Clear locally cached tenant identity. Used when session/break-glass ends.
 * The break-glass session id lives inside ACTIVE_MEMBERSHIP, so removing that
 * single key is enough to wipe the banner state on re-entry.
 */
function clearTenantState(): void {
  localStorage.removeItem(STORAGE_KEYS.ACTIVE_MEMBERSHIP);
  localStorage.removeItem(STORAGE_KEYS.TENANT_SLUG);
}

/**
 * Handle API errors consistently.
 *
 * The backend returns a structured `{ code, returnTo? }` body for 401/403/410
 * so the frontend can react differently for each case rather than treating any
 * 401/403 as "session expired":
 *
 *   - `session_expired` (401)               → clear state, redirect to apex /login
 *   - `break_glass_expired` (403/404)       → clear tenant state, navigate to apex /admin
 *   - `break_glass_hard_cap_reached` (410)  → same as above + show toast
 *   - `forbidden` (403) or no code          → throw, let the caller surface a toast
 *
 * Returning to /admin instead of /login matters: a site-admin whose break-glass
 * just timed out should land back on the admin console, not be sent through the
 * login flow as if their identity itself was invalid.
 */
export async function handleApiError(response: Response): Promise<never> {
  let errorMessage = response.statusText;
  let body: ApiErrorBody | null = null;

  try {
    body = await response.json() as ApiErrorBody;
    errorMessage = body.error || body.message || errorMessage;
  } catch {
    // Response might not be JSON
  }

  const code = body?.code;
  const returnTo = body?.returnTo;

  // Break-glass: route the admin back to /admin instead of /login.
  if (
    code === API_ERROR_CODES.BREAK_GLASS_EXPIRED ||
    code === API_ERROR_CODES.BREAK_GLASS_HARD_CAP_REACHED
  ) {
    clearTenantState();
    if (!navigateToApex(returnTo || "/admin")) {
      // Local dev / no apex — fall back to a same-origin nav.
      window.location.href = returnTo || "/admin";
    }
    throw new Error(errorMessage || "Break-glass session has ended.");
  }

  if (response.status === 401) {
    // Session expired or unauthenticated — clear state and redirect to login.
    clearTenantState();
    redirectToLogin();
    throw new Error(errorMessage || "Your session has expired. Please log in again.");
  }

  throw new Error(`API Error (${response.status}): ${errorMessage}`);
}
