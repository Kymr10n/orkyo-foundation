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
 * Whether the tenant is blocked from normal use but restorable by an owner/admin:
 * suspended, or scheduled for deletion (grace window). Mirrors the backend
 * <c>TenantStatusPolicy.IsBlocked</c>.
 */
export function isBlockedTenantState(state: string | undefined): boolean {
  return state === TENANT_STATUS.SUSPENDED || state === TENANT_STATUS.DELETING;
}

/**
 * Suspension reasons. Keep in sync with backend <c>SuspensionReasonConstants</c>.
 * Blocked ({@link TENANT_STATUS.SUSPENDED}/{@link TENANT_STATUS.DELETING}) memberships carry this.
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
  // Signed in successfully with an identity provider, but nobody has invited this
  // address. Retrying cannot help — the message must say so and point somewhere
  // that can, or the visitor loops on the SSO cookie forever.
  not_invited:
    'Access to Orkyo is currently by invitation only. If your organisation is taking part in the early-access programme, ask your administrator to invite this address — or apply at orkyo.com/design-partners.',
  account_inactive:
    'This account is not active. Please contact your administrator.',
  // Emitted by the SaaS demo-login endpoint when the demo is disabled or Keycloak is
  // unreachable. Without an entry here it fell through to DEFAULT ("Sign-in failed"), which
  // tells a demo visitor — who was never signing in — nothing useful.
  demo_unavailable:
    'The live demo is temporarily unavailable. Please try again shortly.',
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
/**
 * The Resources section — resource types and list definitions.
 *
 * Not '/resources': that prefix belongs to the per-type resource pages
 * (/resources/:typeKey). Administration sets the precedent for a label that differs
 * from its path.
 */
export const ROUTE_CONFIGURATION = '/configuration';

/** The current user's account page (memberships, profile, security). */
export const ROUTE_ACCOUNT = '/account';

/**
 * The rest of the tenant route tree, as absolute paths.
 *
 * Only the live destinations are here — the paths the route table declares AND
 * something else navigates to (the sidebar, the assistant view catalog, the
 * product tour, the floorplan panel). The back-compat redirect sources
 * (`people/teams`, `spaces/list`, `settings/sites`, …) stay inline in
 * `TenantApp`: each appears exactly once and the set is frozen history, so a
 * constant would add a name without removing a duplicate.
 */
export const ROUTE_LOGIN = '/login';
export const ROUTE_HOME = '/';
export const ROUTE_ABOUT = '/about';
export const ROUTE_MESSAGES = '/messages';
export const ROUTE_REQUESTS = '/requests';
export const ROUTE_INSIGHTS = '/insights';
export const ROUTE_STATIONS = '/stations';
export const ROUTE_ASSETS = '/assets';
export const ROUTE_ORGANIZATION = '/organization';

/** Insights tabs. */
export const ROUTE_INSIGHTS_OVERVIEW = `${ROUTE_INSIGHTS}/overview`;
export const ROUTE_INSIGHTS_UTILIZATION = `${ROUTE_INSIGHTS}/utilization`;
export const ROUTE_INSIGHTS_CONFLICTS = `${ROUTE_INSIGHTS}/conflicts`;
export const ROUTE_INSIGHTS_BOTTLENECKS = `${ROUTE_INSIGHTS}/bottlenecks`;

/** The plan surface of the stations page. */
export const ROUTE_STATIONS_FLOORPLAN = `${ROUTE_STATIONS}/floorplan`;

/** The dependency planner for one request's children. */
export const ROUTE_REQUEST_PLAN = `${ROUTE_REQUESTS}/:requestId/plan`;

/**
 * Settings tabs. Absolute, because a nested `<Route>` accepts a child path that
 * starts with its parent's — which is what lets the route table and every place
 * that navigates to a tab share one string. The Insights tabs above set the
 * precedent.
 */
export const ROUTE_SETTINGS_CRITERIA = `${ROUTE_SETTINGS}/criteria`;
export const ROUTE_SETTINGS_TEMPLATES = `${ROUTE_SETTINGS}/templates`;
export const ROUTE_SETTINGS_ROUTINGS = `${ROUTE_SETTINGS}/routings`;
export const ROUTE_SETTINGS_PRESETS = `${ROUTE_SETTINGS}/presets`;
export const ROUTE_SETTINGS_SCHEDULING = `${ROUTE_SETTINGS}/scheduling`;

/** Administration tabs. */
export const ROUTE_TENANT_ADMIN_SITES = `${ROUTE_TENANT_ADMIN}/sites`;
export const ROUTE_TENANT_ADMIN_USERS = `${ROUTE_TENANT_ADMIN}/users`;
export const ROUTE_TENANT_ADMIN_ORGANIZATION = `${ROUTE_TENANT_ADMIN}/organization`;
export const ROUTE_TENANT_ADMIN_CONFIGURATION = `${ROUTE_TENANT_ADMIN}/configuration`;
export const ROUTE_TENANT_ADMIN_INTEGRATIONS = `${ROUTE_TENANT_ADMIN}/integrations`;
export const ROUTE_TENANT_ADMIN_API_ACCESS = `${ROUTE_TENANT_ADMIN}/api-access`;
export const ROUTE_TENANT_ADMIN_AI_ASSISTANT = `${ROUTE_TENANT_ADMIN}/ai-assistant`;
export const ROUTE_TENANT_ADMIN_AUDIT_LOG = `${ROUTE_TENANT_ADMIN}/audit-log`;
export const ROUTE_TENANT_ADMIN_USAGE_LIMITS = `${ROUTE_TENANT_ADMIN}/usage-limits`;

/** Resources tabs. */
export const ROUTE_CONFIGURATION_RESOURCE_TYPES = `${ROUTE_CONFIGURATION}/resource-types`;
export const ROUTE_CONFIGURATION_CATALOG = `${ROUTE_CONFIGURATION}/catalog`;
export const ROUTE_CONFIGURATION_LIST_DEFINITIONS = `${ROUTE_CONFIGURATION}/list-definitions`;

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
