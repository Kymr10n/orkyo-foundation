/**
 * HTTP-related constants.
 * Centralized to prevent string literal duplication across codebase.
 */

import { ApiHeaders } from "@foundation/src/contracts";

/** Header name for tenant context (development/testing) */
export const TENANT_HEADER_NAME = ApiHeaders.TenantSlug;

/** Header name for end-to-end request correlation */
export const CORRELATION_ID_HEADER_NAME = ApiHeaders.CorrelationId;

/** Cookie names used by the frontend */
export const COOKIE_NAMES = {
  /** Shares resolved theme (dark/light) with Keycloak login pages */
  THEME: 'orkyo-theme',
  /** CSRF double-submit token set by the BFF callback (NOT HttpOnly) */
  CSRF: 'orkyo-csrf',
  /** Short-lived cross-subdomain cookie carrying a break-glass session */
  BREAK_GLASS: 'orkyo-break-glass',
} as const;
