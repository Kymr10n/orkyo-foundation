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
  LOADING: 'Loading...',
  REDIRECTING: 'Redirecting...',
  REDIRECTING_LOGIN: 'Redirecting to sign in...',
  SIGNING_OUT: 'Signing out...',
  BACKEND_ERROR_TITLE: 'Something went wrong',
  BACKEND_ERROR_DETAIL: 'Our servers are having trouble — please try again in a moment.',
  NETWORK_ERROR_TITLE: 'Unable to connect',
  NETWORK_ERROR_DETAIL: 'Please check your internet connection and try again.',
  AUTH_ERROR_TITLE: 'Sign-in failed',
} as const;
