/**
 * authMachine — XState v5 state machine for the Orkyo auth pipeline.
 *
 * This is the single source of truth for authentication state and navigation.
 * No auth logic lives in components. Pages send events; the machine decides
 * what happens next.
 *
 * States:
 *   initializing          → fetching session from BFF
 *   unauthenticated       → login failed with error; user may retry
 *   tos_required          → user must accept Terms of Service
 *   no_tenants            → user with no tenants (goes to OnboardingPage)
 *   no_tenants_admin      → site admin with no tenants (goes to AdminPage)
 *   selecting_tenant      → multiple tenants, suspended/deleting only, or site admin hub
 *   redirecting_to_tenant → single active tenant; full-page redirect in prod,
 *                           direct membership set in local dev
 *   redirecting_login     → no session; full-page redirect to BFF login
 *   error_backend         → BFF returned 5xx; user may retry
 *   error_network         → network failure reaching BFF; user may retry
 *   logging_out           → BFF logout in progress
 *   ready                 → membership resolved; app is usable
 *
 * Events (the complete public contract):
 *   LOGIN               → start OIDC flow
 *   LOGOUT              → end session
 *   TOS_ACCEPTED        → re-bootstrap after ToS acceptance
 *   TENANT_CREATED      → re-bootstrap after first tenant creation
 *   TENANT_SELECTED     → user picked a tenant from the list
 *   MEMBERSHIP_SET      → programmatic membership override (break-glass, tenant switch)
 *   MEMBERSHIP_CLEARED  → exit tenant context (break-glass exit, tenant deletion)
 *   USER_UPDATED        → profile update (display name, etc.)
 *   REFRESH             → force re-bootstrap
 *   UNAUTHORIZED        → 401 received from any API call
 *   SESSION_EXPIRED     → session timed out (semantic alias for UNAUTHORIZED)
 *   RETRY               → retry after error_backend / error_network
 */

import { setup, assign, fromPromise } from 'xstate';
import { runtimeConfig } from '@/config/runtime';
import { STORAGE_KEYS } from '@/constants/storage';
import { AUTH_EVENTS, AUTH_MESSAGES, TENANT_STATUS } from '@/constants/auth';
import {
  getCurrentSubdomain,
  navigateToTenantSubdomain,
  consumeBreakGlassCookie,
  getApexOrigin,
} from '@/lib/utils/tenant-navigation';
import type { AppUser, TenantMembership, SessionBootstrapResponse } from '@/contexts/AuthContext';

// ── Types ─────────────────────────────────────────────────────────────────────

interface AuthMachineContext {
  appUser: AppUser | null;
  sessionData: SessionBootstrapResponse | null;
  membership: TenantMembership | null;
  isSiteAdmin: boolean;
  error: string | null;
}

export type AuthMachineEvent =
  | { type: typeof AUTH_EVENTS.LOGIN; returnTo?: string }
  | { type: typeof AUTH_EVENTS.LOGOUT }
  | { type: typeof AUTH_EVENTS.TOS_ACCEPTED }
  | { type: typeof AUTH_EVENTS.TENANT_CREATED }
  | { type: typeof AUTH_EVENTS.TENANT_SELECTED; membership: TenantMembership }
  | { type: typeof AUTH_EVENTS.MEMBERSHIP_SET; membership: TenantMembership }
  | { type: typeof AUTH_EVENTS.MEMBERSHIP_CLEARED }
  | { type: typeof AUTH_EVENTS.SWITCH_TENANT }
  | { type: typeof AUTH_EVENTS.USER_UPDATED; user: AppUser }
  | { type: typeof AUTH_EVENTS.REFRESH }
  | { type: typeof AUTH_EVENTS.UNAUTHORIZED }
  | { type: typeof AUTH_EVENTS.SESSION_EXPIRED }
  | { type: typeof AUTH_EVENTS.REACTIVATE }
  | { type: typeof AUTH_EVENTS.RETRY };

// ── Session bootstrap ──────────────────────────────────────────────────────────

type SessionFetchOutput =
  | { kind: 'empty' }
  | { kind: 'empty_with_error'; error: string }
  | { kind: 'backend_error'; status: number }
  | { kind: 'network_error'; message: string }
  | { kind: 'loaded'; session: SessionBootstrapResponse; membership: TenantMembership | null };

/** Extract the session fetch output from an XState onDone event. */
function getSessionOutput(event: unknown): SessionFetchOutput {
  return (event as { output: SessionFetchOutput }).output;
}

/** Check for BFF error params in the URL (e.g. ?error=identity_link_failed). */
function getUrlAuthError(): string | null {
  const param = new URLSearchParams(window.location.search).get('error');
  return param ? `Authentication error: ${param.replace(/_/g, ' ')}` : null;
}

async function fetchSessionFromBff(): Promise<SessionFetchOutput> {
  let res: Response;

  try {
    res = await fetch(`${runtimeConfig.apiBaseUrl}/api/auth/bff/me`, {
      credentials: 'include',
    });
  } catch {
    return { kind: 'network_error', message: AUTH_MESSAGES.NETWORK_ERROR_DETAIL };
  }

  if (!res.ok) {
    if (res.status >= 500) return { kind: 'backend_error', status: res.status };
    // 401/403 = no valid session
    const urlError = getUrlAuthError();
    return urlError ? { kind: 'empty_with_error', error: urlError } : { kind: 'empty' };
  }

  let data: Record<string, unknown>;
  try {
    data = await res.json();
  } catch {
    return { kind: 'backend_error', status: res.status };
  }

  if (data.authenticated === false || !data.user) {
    const urlError = getUrlAuthError();
    return urlError ? { kind: 'empty_with_error', error: urlError } : { kind: 'empty' };
  }

  const session = data as unknown as SessionBootstrapResponse;

  // Resolve membership — subdomain is the authoritative source in production.
  let membership: TenantMembership | null = null;
  const subdomain = getCurrentSubdomain();

  if (subdomain) {
    const breakGlass = consumeBreakGlassCookie();
    const matching = session.tenants.find(t => t.slug === subdomain);

    if (matching) {
      membership = breakGlass
        ? { ...matching, isBreakGlass: true, breakGlassSessionId: breakGlass.sessionId }
        : matching;
    } else if (session.isSiteAdmin && breakGlass) {
      // Site admin accessing a tenant they're not a member of — break-glass.
      // Requires the cross-subdomain cookie set by the admin panel entry flow;
      // without it there's no backend session to authorize API calls, so we
      // leave membership null and let the guard chain route to the admin panel.
      membership = {
        tenantId: breakGlass.tenantId,
        slug: subdomain,
        displayName: subdomain,
        role: 'admin',
        state: 'active',
        isTenantAdmin: true,
        isBreakGlass: true,
        breakGlassSessionId: breakGlass.sessionId,
      };
    }
  } else if (!runtimeConfig.baseDomain && session.tenants.length === 1) {
    // Local dev: no subdomain routing — auto-select single tenant
    membership = session.tenants[0];
  }

  if (membership) {
    localStorage.setItem(STORAGE_KEYS.ACTIVE_MEMBERSHIP, JSON.stringify(membership));
    localStorage.setItem(STORAGE_KEYS.TENANT_SLUG, membership.slug);
  }

  return { kind: 'loaded', session, membership };
}

// ── BFF logout ────────────────────────────────────────────────────────────────
// S3: GET-based logout — navigate directly to the BFF endpoint which clears the
// session cookie and redirects the browser to Keycloak's end-session URL.

function buildBffLogoutUrl(): string {
  const apexOrigin = `${getApexOrigin()}/`;
  return `${runtimeConfig.apiBaseUrl}/api/auth/bff/logout?returnTo=${encodeURIComponent(apexOrigin)}`;
}

// ── Machine ───────────────────────────────────────────────────────────────────

export const authMachine = setup({
  types: {
    context: {} as AuthMachineContext,
    events: {} as AuthMachineEvent,
  },

  actors: {
    fetchSession: fromPromise<SessionFetchOutput>(async () => fetchSessionFromBff()),
    performLogout: fromPromise<{ logoutUrl: string | null }>(async () => ({ logoutUrl: buildBffLogoutUrl() })),
  },

  actions: {
    // Populate context from a successful session fetch
    assignSession: assign(({ event }) => {
      const output = getSessionOutput(event);
      if (output.kind !== 'loaded') return {};
      return {
        appUser: output.session.user,
        sessionData: output.session,
        membership: output.membership,
        isSiteAdmin: output.session.isSiteAdmin ?? false,
        error: null,
      };
    }),

    clearSession: assign({
      appUser: null,
      sessionData: null,
      membership: null,
      isSiteAdmin: false,
      error: null,
    }),

    clearStorage: () => {
      localStorage.removeItem(STORAGE_KEYS.ACTIVE_MEMBERSHIP);
      localStorage.removeItem(STORAGE_KEYS.TENANT_SLUG);
    },

    // Redirect to BFF login endpoint (full-page, browser handles OIDC)
    performLogin: ({ event }) => {
      const returnTo = event.type === AUTH_EVENTS.LOGIN
        ? ((event as { returnTo?: string }).returnTo ?? window.location.href)
        : window.location.href;
      window.location.href = `${runtimeConfig.apiBaseUrl}/api/auth/bff/login?returnTo=${encodeURIComponent(returnTo)}`;
    },

    // Full-page redirect to the tenant subdomain. In local dev, navigateToTenantSubdomain
    // returns false (no baseDomain), so the `isLocalDev` always-guard takes over instead.
    performRedirectToTenant: ({ context }) => {
      const slug = context.membership?.slug ?? context.sessionData?.tenants[0]?.slug;
      if (slug) navigateToTenantSubdomain(slug, '/');
    },

    // Persist membership to localStorage (side effect — separate from the assign below)
    persistMembership: ({ event }) => {
      if (event.type !== AUTH_EVENTS.TENANT_SELECTED && event.type !== AUTH_EVENTS.MEMBERSHIP_SET) return;
      const m = (event as { membership: TenantMembership }).membership;
      localStorage.setItem(STORAGE_KEYS.ACTIVE_MEMBERSHIP, JSON.stringify(m));
      localStorage.setItem(STORAGE_KEYS.TENANT_SLUG, m.slug);
    },

    // Assign the selected membership into context (pure)
    assignSelectedMembership: assign(({ event }) => {
      if (event.type !== AUTH_EVENTS.TENANT_SELECTED && event.type !== AUTH_EVENTS.MEMBERSHIP_SET) return {};
      return { membership: (event as { membership: TenantMembership }).membership };
    }),

    clearMembershipFromContext: assign({ membership: null }),

    assignUserUpdate: assign(({ event }) => {
      if (event.type !== AUTH_EVENTS.USER_UPDATED) return {};
      return { appUser: (event as { user: AppUser }).user };
    }),

    // Assign an error from a failed session fetch (login error, backend error, or network error)
    assignFetchError: assign(({ event }) => {
      const output = getSessionOutput(event);
      let error: string;
      if (output.kind === 'empty_with_error') error = output.error;
      else if (output.kind === 'backend_error') error = `Server error (${output.status})`;
      else if (output.kind === 'network_error') error = output.message;
      else error = AUTH_MESSAGES.NETWORK_ERROR_DETAIL;
      return { appUser: null, sessionData: null, membership: null, isSiteAdmin: false, error };
    }),

    // Navigate to Keycloak logout URL, or fall back to /login
    redirectAfterLogout: ({ event }) => {
      const { logoutUrl } = (event as unknown as { output: { logoutUrl: string | null } }).output;
      window.location.href = logoutUrl ?? '/login';
    },

    redirectToLoginFallback: () => {
      window.location.href = '/login';
    },
  },

  guards: {
    networkError: ({ event }) => getSessionOutput(event).kind === 'network_error',
    backendError: ({ event }) => getSessionOutput(event).kind === 'backend_error',
    sessionEmptyWithError: ({ event }) => getSessionOutput(event).kind === 'empty_with_error',
    sessionEmpty: ({ event }) => getSessionOutput(event).kind === 'empty',

    tosRequired: ({ event }) => {
      const output = getSessionOutput(event);
      return output.kind === 'loaded' && output.session.tosRequired;
    },
    noTenantsAdmin: ({ event }) => {
      const output = getSessionOutput(event);
      return output.kind === 'loaded' && !!output.session.isSiteAdmin && output.session.tenants.length === 0;
    },
    noTenants: ({ event }) => {
      const output = getSessionOutput(event);
      return output.kind === 'loaded' && output.session.tenants.length === 0;
    },
    membershipResolved: ({ event }) => {
      const output = getSessionOutput(event);
      return output.kind === 'loaded' && output.membership !== null && output.membership.state !== TENANT_STATUS.SUSPENDED;
    },
    singleActiveTenant: ({ event }) => {
      const output = getSessionOutput(event);
      if (output.kind !== 'loaded') return false;
      const tenants = output.session.tenants;
      return tenants.length === 1 && tenants[0].state === TENANT_STATUS.ACTIVE;
    },
    // No baseDomain = local dev. navigateToTenantSubdomain() is a no-op here,
    // so we transition directly to ready using the already-assigned membership.
    isLocalDev: () => !runtimeConfig.baseDomain,
  },
}).createMachine({
  id: 'auth',
  initial: 'initializing',
  context: {
    appUser: null,
    sessionData: null,
    membership: null,
    isSiteAdmin: false,
    error: null,
  },

  states: {
    // ── Bootstrap ────────────────────────────────────────────────────────────
    initializing: {
      invoke: {
        id: 'fetchSession',
        src: 'fetchSession',
        onDone: [
          { guard: 'networkError',          target: 'error_network',          actions: 'assignFetchError' },
          { guard: 'backendError',          target: 'error_backend',          actions: 'assignFetchError' },
          { guard: 'sessionEmptyWithError', target: 'unauthenticated',        actions: 'assignFetchError' },
          { guard: 'sessionEmpty',          target: 'redirecting_login',     actions: 'clearSession' },
          { guard: 'tosRequired',           target: 'tos_required',          actions: 'assignSession' },
          // membershipResolved must precede noTenants: a site admin entering
          // a tenant via break-glass has tenants.length === 0 but
          // fetchSessionFromBff synthesises a membership, so they belong in
          // `ready`, not routed back to the onboarding pipeline.
          { guard: 'membershipResolved',    target: 'ready',                 actions: 'assignSession' },
          { guard: 'noTenantsAdmin',        target: 'no_tenants_admin',      actions: 'assignSession' },
          { guard: 'noTenants',             target: 'no_tenants',            actions: 'assignSession' },
          // Single active tenant with no resolved membership → redirect.
          // Everything else (multi-tenant, single suspended/deleting, site
          // admin with all suspended) → tenant selector as central hub.
          { guard: 'singleActiveTenant',    target: 'redirecting_to_tenant', actions: 'assignSession' },
          {                                 target: 'selecting_tenant',      actions: 'assignSession' },
        ],
        onError: {
          target: 'error_network',
          actions: 'clearSession',
        },
      },
    },

    // ── Login failed with error ──────────────────────────────────────────────
    unauthenticated: {
      entry: 'clearStorage',
      on: {
        [AUTH_EVENTS.LOGIN]: { target: 'redirecting_login' },
      },
    },

    // ── Redirecting to BFF login (full-page redirect, no session) ────────────
    redirecting_login: {
      entry: ['clearStorage', 'performLogin'],
    },

    // ── Error states (non-auth failures) ─────────────────────────────────────
    error_backend: {
      on: {
        [AUTH_EVENTS.RETRY]: { target: 'initializing' },
      },
    },

    error_network: {
      on: {
        [AUTH_EVENTS.RETRY]: { target: 'initializing' },
      },
    },

    // ── Auth pipeline stages ─────────────────────────────────────────────────
    tos_required: {
      on: {
        [AUTH_EVENTS.TOS_ACCEPTED]: { target: 'initializing' },
        [AUTH_EVENTS.LOGOUT]:       { target: 'logging_out' },
      },
    },



    no_tenants: {
      on: {
        [AUTH_EVENTS.TENANT_CREATED]: { target: 'initializing' },
        [AUTH_EVENTS.LOGOUT]:         { target: 'logging_out' },
      },
    },

    no_tenants_admin: {
      on: {
        [AUTH_EVENTS.MEMBERSHIP_SET]: {
          target: 'ready',
          actions: ['persistMembership', 'assignSelectedMembership'],
        },
        [AUTH_EVENTS.REFRESH]: { target: 'initializing' },
        [AUTH_EVENTS.LOGOUT]:  { target: 'logging_out' },
      },
    },

    selecting_tenant: {
      on: {
        [AUTH_EVENTS.TENANT_SELECTED]: {
          target: 'redirecting_to_tenant',
          actions: ['persistMembership', 'assignSelectedMembership'],
        },
        [AUTH_EVENTS.MEMBERSHIP_SET]: {
          target: 'ready',
          actions: ['persistMembership', 'assignSelectedMembership'],
        },
        [AUTH_EVENTS.REACTIVATE]: { target: 'initializing' },
        [AUTH_EVENTS.REFRESH]:    { target: 'initializing' },
        [AUTH_EVENTS.LOGOUT]:     { target: 'logging_out' },
      },
    },

    // Full-page redirect in production. In local dev, isLocalDev guard fires
    // immediately and transitions to ready (membership already set in context).
    redirecting_to_tenant: {
      entry: 'performRedirectToTenant',
      always: [
        { guard: 'isLocalDev', target: 'ready' },
      ],
      on: {
        // Belt-and-suspenders: explicit local dev path
        [AUTH_EVENTS.MEMBERSHIP_SET]: {
          target: 'ready',
          actions: ['persistMembership', 'assignSelectedMembership'],
        },
      },
    },

    // ── Logout ───────────────────────────────────────────────────────────────
    // Stay in logging_out after the BFF call — the redirect actions navigate
    // the browser away.  Transitioning to unauthenticated would trigger
    // TenantApp's auto-login effect and abort the Keycloak logout redirect.
    logging_out: {
      entry: ['clearSession', 'clearStorage'],
      invoke: {
        src: 'performLogout',
        onDone: {
          actions: 'redirectAfterLogout',
        },
        onError: {
          actions: 'redirectToLoginFallback',
        },
      },
    },

    // ── App ready ────────────────────────────────────────────────────────────
    ready: {
      on: {
        [AUTH_EVENTS.LOGOUT]:             { target: 'logging_out' },
        [AUTH_EVENTS.UNAUTHORIZED]:       { target: 'redirecting_login', actions: ['clearSession', 'clearStorage'] },
        [AUTH_EVENTS.SESSION_EXPIRED]:    { target: 'redirecting_login', actions: ['clearSession', 'clearStorage'] },
        [AUTH_EVENTS.REFRESH]:            { target: 'initializing' },
        // Tenant switch or break-glass enter — caller handles navigation after
        [AUTH_EVENTS.MEMBERSHIP_SET]:     { actions: ['persistMembership', 'assignSelectedMembership'] },
        // Break-glass exit or tenant deletion — caller navigates away after
        [AUTH_EVENTS.MEMBERSHIP_CLEARED]: { actions: ['clearMembershipFromContext', 'clearStorage'] },
        // Multi-tenant user returning to the selector (local dev only; production
        // navigates to apex and the machine re-bootstraps there instead).
        [AUTH_EVENTS.SWITCH_TENANT]:      { target: 'selecting_tenant', actions: ['clearMembershipFromContext', 'clearStorage'] },
        // Profile update (e.g. display name change)
        [AUTH_EVENTS.USER_UPDATED]:       { actions: 'assignUserUpdate' },
      },
    },
  },
});
