/**
 * Auth pipeline constants.
 *
 * Single source of truth for auth stage names, machine event types,
 * and user-facing messages. Consumed by authMachine, AuthContext,
 * ApexGateway, RequireAuth, and all page components.
 */

// ── Auth pipeline stages ──────────────────────────────────────────────────────

/** Auth pipeline stages — corresponds to XState machine state names. */
export const AUTH_STAGES = {
  INITIALIZING: 'initializing',
  UNAUTHENTICATED: 'unauthenticated',
  TOS_REQUIRED: 'tos_required',
  NO_TENANTS: 'no_tenants',
  NO_TENANTS_ADMIN: 'no_tenants_admin',
  SELECTING_TENANT: 'selecting_tenant',
  REDIRECTING_TO_TENANT: 'redirecting_to_tenant',
  REDIRECTING_LOGIN: 'redirecting_login',
  LOGGING_OUT: 'logging_out',
  READY: 'ready',
  ERROR_BACKEND: 'error_backend',
  ERROR_NETWORK: 'error_network',
} as const;

/**
 * Auth pipeline stage — derived from AUTH_STAGES values.
 * Used by ApexGateway, RequireAuth, App, and TenantApp.
 */
export type AuthStage = typeof AUTH_STAGES[keyof typeof AUTH_STAGES];

/** Stages where a loading indicator should be shown rather than content. */
export const LOADING_STAGES: ReadonlySet<AuthStage> = new Set([
  AUTH_STAGES.INITIALIZING,
  AUTH_STAGES.LOGGING_OUT,
  AUTH_STAGES.REDIRECTING_LOGIN,
]);

/** Stages where the user is NOT considered authenticated. */
export const UNAUTHENTICATED_STAGES: ReadonlySet<AuthStage> = new Set([
  AUTH_STAGES.INITIALIZING,
  AUTH_STAGES.UNAUTHENTICATED,
  AUTH_STAGES.LOGGING_OUT,
  AUTH_STAGES.ERROR_BACKEND,
  AUTH_STAGES.ERROR_NETWORK,
  AUTH_STAGES.REDIRECTING_LOGIN,
]);

// ── Tenant lifecycle values ───────────────────────────────────────────────────

/**
 * Tenant status values returned by the BFF. Mirrors the backend
 * <c>TenantStatusConstants</c> and the DB check constraint.
 */
export const TENANT_STATUS = {
  ACTIVE: 'active',
  SUSPENDED: 'suspended',
  PENDING: 'pending',
  DELETED: 'deleted',
  DELETING: 'deleting',
} as const;

/**
 * Suspension reasons. Keep in sync with backend <c>SuspensionReasonConstants</c>.
 * Only {@link TENANT_STATUS.SUSPENDED} memberships carry this.
 */
export const SUSPENSION_REASON = {
  INACTIVITY: 'inactivity',
  MANUAL_ADMIN: 'manual_admin',
  PAYMENT_OVERDUE: 'payment_overdue',
  SECURITY_INCIDENT: 'security_incident',
  TRIAL_EXPIRED: 'trial_expired',
  COMPLIANCE_HOLD: 'compliance_hold',
} as const;

// ── Auth machine event types ──────────────────────────────────────────────────

/** Auth machine event type names. */
export const AUTH_EVENTS = {
  LOGIN: 'LOGIN',
  LOGOUT: 'LOGOUT',
  TOS_ACCEPTED: 'TOS_ACCEPTED',
  TENANT_CREATED: 'TENANT_CREATED',
  TENANT_SELECTED: 'TENANT_SELECTED',
  MEMBERSHIP_SET: 'MEMBERSHIP_SET',
  MEMBERSHIP_CLEARED: 'MEMBERSHIP_CLEARED',
  SWITCH_TENANT: 'SWITCH_TENANT',
  USER_UPDATED: 'USER_UPDATED',
  REFRESH: 'REFRESH',
  UNAUTHORIZED: 'UNAUTHORIZED',
  SESSION_EXPIRED: 'SESSION_EXPIRED',
  REACTIVATE: 'REACTIVATE',
  RETRY: 'RETRY',
} as const;

// ── User-facing messages ──────────────────────────────────────────────────────

/** Spinner and status messages shown to the user. */
export const AUTH_MESSAGES = {
  LOADING: 'Loading…',
  REDIRECTING: 'Redirecting...',
  REDIRECTING_LOGIN: 'Redirecting to sign in...',
  SIGNING_OUT: 'Signing out...',
  BACKEND_ERROR_TITLE: 'Something went wrong',
  BACKEND_ERROR_DETAIL: 'Our servers are having trouble — please try again in a moment.',
  NETWORK_ERROR_TITLE: 'Unable to connect',
  NETWORK_ERROR_DETAIL: 'Please check your internet connection and try again.',
  AUTH_ERROR_TITLE: 'Sign-in failed',
} as const;

/**
 * User-facing messages for BFF auth error codes returned via the `?error=` query
 * param (e.g. `/login?error=invalid_state`). `DEFAULT` is the fallback for any
 * unrecognized code. Consumed by `getUrlAuthError` in the auth machine.
 */
export const AUTH_ERROR_MESSAGES = {
  identity_link_failed:
    'We could not link your identity. Please try signing in again or contact support.',
  auth_failed:
    'Sign-in failed. Please try again. If the problem persists, contact support.',
  invalid_state: 'Your sign-in session expired. Please try again.',
  DEFAULT: 'Sign-in failed. Please try again.',
} as const;

// ── Routing ───────────────────────────────────────────────────────────────────

/** Public route paths that render without an authenticated session. */
export const ROUTE_SIGNUP = '/signup';
export const ROUTE_CREATE_ACCOUNT = '/create-account';

/**
 * Platform-operator admin panel route (site-admins, `isSiteAdmin`). The page
 * itself is SaaS-injected via `renderAdminPage`; foundation only owns the route
 * string, navigation, and break-glass return target. Distinct from the tenant
 * `/tenant-admin` Administration page (tenant `role === "admin"`).
 */
export const ROUTE_SITE_ADMIN = '/site-admin';

/** Editor-open Settings area (criteria, templates, presets, scheduling). */
export const ROUTE_SETTINGS = '/settings';

/** Tenant-admin Administration area (sites, users, organization, …). */
export const ROUTE_TENANT_ADMIN = '/tenant-admin';

/** The current user's account page (memberships, profile, security). */
export const ROUTE_ACCOUNT = '/account';

/**
 * Paths that render without an authenticated session by design (invitation
 * signup, request-access). The auth machine must never redirect these to the
 * BFF login endpoint, and `ApexGateway` renders them ahead of the auth pipeline.
 */
export const PUBLIC_PATHS = [ROUTE_SIGNUP, ROUTE_CREATE_ACCOUNT] as const;

/** True when `pathname` is one of {@link PUBLIC_PATHS} (or a sub-path of one). */
export function isPublicPath(pathname: string): boolean {
  return PUBLIC_PATHS.some((p) => pathname === p || pathname.startsWith(`${p}/`));
}
