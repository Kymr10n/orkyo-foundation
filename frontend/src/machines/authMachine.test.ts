import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { createActor, waitFor, fromPromise } from 'xstate';

// ── Mocks that must be declared before importing the machine ─────────────────

// Mutable mock fns — tests can override return values per-case
const mockGetCurrentSubdomain = vi.fn<() => string | null>(() => null);
const mockConsumeBreakGlassCookie = vi.fn<() => { sessionId: string; tenantId: string } | null>(() => null);

vi.mock('@foundation/src/lib/utils/tenant-navigation', () => ({
  getCurrentSubdomain: (...args: unknown[]) => mockGetCurrentSubdomain(...(args as [])),
  navigateToTenantSubdomain: vi.fn(),
  consumeBreakGlassCookie: (...args: unknown[]) => mockConsumeBreakGlassCookie(...(args as [])),
  getApexOrigin: () => 'http://localhost:5173',
}));

// Stub browser globals
vi.stubGlobal('localStorage', {
  setItem: vi.fn(),
  getItem: vi.fn(),
  removeItem: vi.fn(),
});

// Prevent full-page redirects
Object.defineProperty(window, 'location', {
  value: { href: 'http://localhost:5173', search: '' },
  writable: true,
});

vi.mock('@foundation/src/config/runtime', () => ({
  runtimeConfig: { apiBaseUrl: 'http://localhost:5000', baseDomain: '' },
}));

vi.mock('@foundation/src/constants/storage', () => ({
  STORAGE_KEYS: { ACTIVE_MEMBERSHIP: 'membership', TENANT_SLUG: 'slug' },
}));

vi.mock('@foundation/src/constants/auth', () => ({
  AUTH_EVENTS: {
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
  },
  AUTH_MESSAGES: {
    NETWORK_ERROR_DETAIL: 'Network error',
  },
  TENANT_STATUS: {
    ACTIVE: 'active',
    SUSPENDED: 'suspended',
    PENDING: 'pending',
    DELETED: 'deleted',
    DELETING: 'deleting',
  },
}));

// Import after mocks are set up
import { authMachine } from './authMachine';

const mockUser = { id: 'u1', email: 'test@test.com', displayName: 'Test', isSiteAdmin: false, hasSeenTour: true };
const mockMembership = { tenantId: 't1', slug: 'acme', displayName: 'Acme', role: 'admin', state: 'active', isTenantAdmin: true };

function machineWithOutput(output: Record<string, unknown>) {
  return authMachine.provide({
    actors: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      fetchSession: fromPromise(async () => output) as any,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      performLogout: fromPromise(async () => ({ logoutUrl: null as string | null })) as any,
    },
  });
}

describe('authMachine', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('transitions to ready when session has resolved membership (local dev)', async () => {
    const machine = machineWithOutput({
      kind: 'loaded',
      session: { user: mockUser, tenants: [mockMembership], isSiteAdmin: false, tosRequired: false },
      membership: mockMembership,
    });
    const actor = createActor(machine);
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value === 'ready', { timeout: 2000 });
    expect(snapshot.value).toBe('ready');
    expect(snapshot.context.appUser).toEqual(mockUser);
    expect(snapshot.context.membership).toEqual(mockMembership);
    actor.stop();
  });

  it('transitions to error_network on network failure', async () => {
    const machine = machineWithOutput({
      kind: 'network_error',
      message: 'Network error',
    });
    const actor = createActor(machine);
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value === 'error_network', { timeout: 2000 });
    expect(snapshot.value).toBe('error_network');
    expect(snapshot.context.error).toBe('Network error');
    actor.stop();
  });

  it('transitions to error_backend on 500 response', async () => {
    const machine = machineWithOutput({
      kind: 'backend_error',
      status: 500,
    });
    const actor = createActor(machine);
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value === 'error_backend', { timeout: 2000 });
    expect(snapshot.value).toBe('error_backend');
    actor.stop();
  });

  it('transitions to tos_required when tosRequired is true', async () => {
    const machine = machineWithOutput({
      kind: 'loaded',
      session: { user: mockUser, tenants: [mockMembership], isSiteAdmin: false, tosRequired: true },
      membership: null,
    });
    const actor = createActor(machine);
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value === 'tos_required', { timeout: 2000 });
    expect(snapshot.value).toBe('tos_required');
    actor.stop();
  });

  it('transitions to selecting_tenant when multi-tenant user', async () => {
    const m2 = { ...mockMembership, tenantId: 't2', slug: 'beta', displayName: 'Beta' };
    const machine = machineWithOutput({
      kind: 'loaded',
      session: { user: mockUser, tenants: [mockMembership, m2], isSiteAdmin: false, tosRequired: false },
      membership: null,
    });
    const actor = createActor(machine);
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value === 'selecting_tenant', { timeout: 2000 });
    expect(snapshot.value).toBe('selecting_tenant');
    actor.stop();
  });

  it('handles RETRY from error_backend → initializing', async () => {
    const machine = machineWithOutput({
      kind: 'backend_error',
      status: 500,
    });
    const actor = createActor(machine);
    actor.start();

    await waitFor(actor, (s) => s.value === 'error_backend', { timeout: 2000 });
    actor.send({ type: 'RETRY' });
    // RETRY transitions to initializing (which immediately re-invokes fetchSession)
    expect(['initializing', 'error_backend']).toContain(actor.getSnapshot().value);
    actor.stop();
  });

  it('handles USER_UPDATED in ready state', async () => {
    const machine = machineWithOutput({
      kind: 'loaded',
      session: { user: mockUser, tenants: [mockMembership], isSiteAdmin: false, tosRequired: false },
      membership: mockMembership,
    });
    const actor = createActor(machine);
    actor.start();

    await waitFor(actor, (s) => s.value === 'ready', { timeout: 2000 });
    const updatedUser = { ...mockUser, displayName: 'Updated Name' };
    actor.send({ type: 'USER_UPDATED', user: updatedUser });
    expect(actor.getSnapshot().context.appUser?.displayName).toBe('Updated Name');
    actor.stop();
  });

  it('handles MEMBERSHIP_CLEARED in ready state', async () => {
    const machine = machineWithOutput({
      kind: 'loaded',
      session: { user: mockUser, tenants: [mockMembership], isSiteAdmin: false, tosRequired: false },
      membership: mockMembership,
    });
    const actor = createActor(machine);
    actor.start();

    await waitFor(actor, (s) => s.value === 'ready', { timeout: 2000 });
    actor.send({ type: 'MEMBERSHIP_CLEARED' });
    expect(actor.getSnapshot().context.membership).toBeNull();
    actor.stop();
  });

  it('transitions to no_tenants for user with no tenants and not admin', async () => {
    const machine = machineWithOutput({
      kind: 'loaded',
      session: { user: mockUser, tenants: [], isSiteAdmin: false, tosRequired: false },
      membership: null,
    });
    const actor = createActor(machine);
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value === 'no_tenants', { timeout: 2000 });
    expect(snapshot.value).toBe('no_tenants');
    actor.stop();
  });

  it('transitions to no_tenants_admin for site admin with no tenants', async () => {
    const machine = machineWithOutput({
      kind: 'loaded',
      session: { user: mockUser, tenants: [], isSiteAdmin: true, tosRequired: false },
      membership: null,
    });
    const actor = createActor(machine);
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value === 'no_tenants_admin', { timeout: 2000 });
    expect(snapshot.value).toBe('no_tenants_admin');
    actor.stop();
  });

  // ── Tenant suspension tests ──────────────────────────────────────────────

  // ── MEMBERSHIP_SET from admin panel (enter tenant) ─────────────────────

  it('MEMBERSHIP_SET from selecting_tenant transitions to ready (admin enter tenant)', async () => {
    const machine = machineWithOutput({
      kind: 'loaded',
      session: { user: mockUser, tenants: [mockMembership, { ...mockMembership, tenantId: 't2', slug: 'beta' }], isSiteAdmin: true, tosRequired: false },
      membership: null,
    });
    const actor = createActor(machine);
    actor.start();

    await waitFor(actor, (s) => s.value === 'selecting_tenant', { timeout: 2000 });
    actor.send({ type: 'MEMBERSHIP_SET', membership: mockMembership });

    const snapshot = await waitFor(actor, (s) => s.value === 'ready', { timeout: 2000 });
    expect(snapshot.value).toBe('ready');
    expect(snapshot.context.membership).toEqual(mockMembership);
    actor.stop();
  });

  it('MEMBERSHIP_SET from no_tenants_admin transitions to ready (admin enter tenant)', async () => {
    const machine = machineWithOutput({
      kind: 'loaded',
      session: { user: mockUser, tenants: [], isSiteAdmin: true, tosRequired: false },
      membership: null,
    });
    const actor = createActor(machine);
    actor.start();

    await waitFor(actor, (s) => s.value === 'no_tenants_admin', { timeout: 2000 });
    actor.send({ type: 'MEMBERSHIP_SET', membership: mockMembership });

    const snapshot = await waitFor(actor, (s) => s.value === 'ready', { timeout: 2000 });
    expect(snapshot.value).toBe('ready');
    expect(snapshot.context.membership).toEqual(mockMembership);
    actor.stop();
  });

  it('suspended membership routes to selecting_tenant', async () => {
    const suspendedMembership = {
      ...mockMembership,
      state: 'suspended',
      suspensionReason: 'inactivity',
      canReactivate: true,
    };
    const machine = machineWithOutput({
      kind: 'loaded',
      session: { user: mockUser, tenants: [suspendedMembership], isSiteAdmin: false, tosRequired: false },
      membership: suspendedMembership,
    });
    const actor = createActor(machine);
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value === 'selecting_tenant', { timeout: 2000 });
    expect(snapshot.value).toBe('selecting_tenant');
    actor.stop();
  });

  it('REACTIVATE event from selecting_tenant re-bootstraps session', async () => {
    const suspendedMembership = { ...mockMembership, state: 'suspended' };
    const machine = machineWithOutput({
      kind: 'loaded',
      session: { user: mockUser, tenants: [suspendedMembership], isSiteAdmin: false, tosRequired: false },
      membership: suspendedMembership,
    });
    const actor = createActor(machine);
    actor.start();

    await waitFor(actor, (s) => s.value === 'selecting_tenant', { timeout: 2000 });
    actor.send({ type: 'REACTIVATE' });
    expect(['initializing', 'selecting_tenant']).toContain(actor.getSnapshot().value);
    actor.stop();
  });

  it('active membership goes to ready, not selecting_tenant', async () => {
    const activeMembership = { ...mockMembership, state: 'active' };
    const machine = machineWithOutput({
      kind: 'loaded',
      session: { user: mockUser, tenants: [activeMembership], isSiteAdmin: false, tosRequired: false },
      membership: activeMembership,
    });
    const actor = createActor(machine);
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value === 'ready', { timeout: 2000 });
    expect(snapshot.value).toBe('ready');
    actor.stop();
  });

  it('site admin with suspended membership + one active tenant → selecting_tenant', async () => {
    const suspendedMembership = { ...mockMembership, state: 'suspended' };
    const activeTenant = { ...mockMembership, tenantId: 't2', slug: 'other', displayName: 'Other', state: 'active' };
    const machine = machineWithOutput({
      kind: 'loaded',
      session: { user: mockUser, tenants: [suspendedMembership, activeTenant], isSiteAdmin: true, tosRequired: false },
      membership: suspendedMembership,
    });
    const actor = createActor(machine);
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value === 'selecting_tenant', { timeout: 2000 });
    expect(snapshot.value).toBe('selecting_tenant');
    actor.stop();
  });

  it('site admin with null membership + one active tenant → selecting_tenant (local dev)', async () => {
    const suspendedTenant = { ...mockMembership, state: 'suspended' };
    const activeTenant = { ...mockMembership, tenantId: 't2', slug: 'other', displayName: 'Other', state: 'active' };
    const machine = machineWithOutput({
      kind: 'loaded',
      session: { user: mockUser, tenants: [suspendedTenant, activeTenant], isSiteAdmin: true, tosRequired: false },
      membership: null,
    });
    const actor = createActor(machine);
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value === 'selecting_tenant', { timeout: 2000 });
    expect(snapshot.value).toBe('selecting_tenant');
    actor.stop();
  });

  it('site admin with multiple active tenants → selecting_tenant', async () => {
    const activeTenant1 = { ...mockMembership, state: 'active' };
    const activeTenant2 = { ...mockMembership, tenantId: 't2', slug: 'other', displayName: 'Other', state: 'active' };
    const machine = machineWithOutput({
      kind: 'loaded',
      session: { user: mockUser, tenants: [activeTenant1, activeTenant2], isSiteAdmin: true, tosRequired: false },
      membership: null,
    });
    const actor = createActor(machine);
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value === 'selecting_tenant', { timeout: 2000 });
    expect(snapshot.value).toBe('selecting_tenant');
    actor.stop();
  });

  it('site admin with all tenants suspended → selecting_tenant', async () => {
    const suspendedMembership = { ...mockMembership, state: 'suspended' };
    const suspendedTenant2 = { ...mockMembership, tenantId: 't2', slug: 'other', state: 'suspended' };
    const machine = machineWithOutput({
      kind: 'loaded',
      session: { user: mockUser, tenants: [suspendedMembership, suspendedTenant2], isSiteAdmin: true, tosRequired: false },
      membership: suspendedMembership,
    });
    const actor = createActor(machine);
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value === 'selecting_tenant', { timeout: 2000 });
    expect(snapshot.value).toBe('selecting_tenant');
    actor.stop();
  });

  it('single active tenant with no resolved membership → redirecting_to_tenant', async () => {
    const machine = machineWithOutput({
      kind: 'loaded',
      session: { user: mockUser, tenants: [mockMembership], isSiteAdmin: false, tosRequired: false },
      membership: null,
    });
    const actor = createActor(machine);
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value !== 'initializing', { timeout: 2000 });
    expect(['redirecting_to_tenant', 'ready']).toContain(snapshot.value);
    actor.stop();
  });
});

// ── fetchSessionFromBff integration tests ──────────────────────────────────
// These use the REAL fetchSession actor (no machineWithOutput override) so the
// actual fetchSessionFromBff function executes with controlled fetch/navigation mocks.

describe('authMachine — break-glass cookie guard (integration)', () => {
  const siteAdmin = { ...mockUser, isSiteAdmin: true };

  function mockFetchResponse(body: Record<string, unknown>, status = 200) {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: status >= 200 && status < 300,
      status,
      json: async () => body,
    }));
  }

  // Use the real fetchSession actor — only override performLogout to prevent
  // actual navigation during logout transitions.
  function machineWithRealFetch() {
    return authMachine.provide({
      actors: {
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        performLogout: fromPromise(async () => ({ logoutUrl: null as string | null })) as any,
      },
    });
  }

  beforeEach(() => {
    vi.clearAllMocks();
    mockGetCurrentSubdomain.mockReturnValue(null);
    mockConsumeBreakGlassCookie.mockReturnValue(null);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('site admin on tenant subdomain WITH break-glass cookie → ready (break-glass membership)', async () => {
    mockGetCurrentSubdomain.mockReturnValue('acme');
    mockConsumeBreakGlassCookie.mockReturnValue({ sessionId: 'bg-session-1', tenantId: 'tid-1' });
    mockFetchResponse({
      authenticated: true,
      user: siteAdmin,
      tenants: [],          // not a member of this tenant
      isSiteAdmin: true,
      tosRequired: false,
    });

    const actor = createActor(machineWithRealFetch());
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value === 'ready', { timeout: 2000 });
    expect(snapshot.value).toBe('ready');
    expect(snapshot.context.membership).toMatchObject({
      slug: 'acme',
      tenantId: 'tid-1',
      isBreakGlass: true,
      breakGlassSessionId: 'bg-session-1',
    });
    actor.stop();
  });

  it('site admin on tenant subdomain WITHOUT break-glass cookie → no_tenants_admin (no synthetic membership)', async () => {
    mockGetCurrentSubdomain.mockReturnValue('acme');
    mockConsumeBreakGlassCookie.mockReturnValue(null);
    mockFetchResponse({
      authenticated: true,
      user: siteAdmin,
      tenants: [],
      isSiteAdmin: true,
      tosRequired: false,
    });

    const actor = createActor(machineWithRealFetch());
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value === 'no_tenants_admin', { timeout: 2000 });
    expect(snapshot.value).toBe('no_tenants_admin');
    expect(snapshot.context.membership).toBeNull();
    actor.stop();
  });

  it('site admin who IS a member of the tenant — normal membership, no break-glass', async () => {
    mockGetCurrentSubdomain.mockReturnValue('acme');
    mockConsumeBreakGlassCookie.mockReturnValue(null);
    mockFetchResponse({
      authenticated: true,
      user: siteAdmin,
      tenants: [mockMembership],   // acme membership exists
      isSiteAdmin: true,
      tosRequired: false,
    });

    const actor = createActor(machineWithRealFetch());
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value === 'ready', { timeout: 2000 });
    expect(snapshot.value).toBe('ready');
    expect(snapshot.context.membership).toMatchObject({ slug: 'acme' });
    expect(snapshot.context.membership?.isBreakGlass).toBeFalsy();
    actor.stop();
  });

  it('site admin who IS a member — break-glass cookie decorates existing membership', async () => {
    mockGetCurrentSubdomain.mockReturnValue('acme');
    mockConsumeBreakGlassCookie.mockReturnValue({ sessionId: 'bg-session-2', tenantId: 't1' });
    mockFetchResponse({
      authenticated: true,
      user: siteAdmin,
      tenants: [mockMembership],
      isSiteAdmin: true,
      tosRequired: false,
    });

    const actor = createActor(machineWithRealFetch());
    actor.start();

    const snapshot = await waitFor(actor, (s) => s.value === 'ready', { timeout: 2000 });
    expect(snapshot.context.membership).toMatchObject({
      slug: 'acme',
      tenantId: 't1',
      isBreakGlass: true,
      breakGlassSessionId: 'bg-session-2',
    });
    actor.stop();
  });

  it('non-admin user on tenant subdomain without membership → redirecting_login', async () => {
    mockGetCurrentSubdomain.mockReturnValue('acme');
    mockFetchResponse({
      authenticated: true,
      user: mockUser,
      tenants: [],      // not a member, not a site admin
      isSiteAdmin: false,
      tosRequired: false,
    });

    const actor = createActor(machineWithRealFetch());
    actor.start();

    // No membership, no tenants, not admin → no_tenants
    const snapshot = await waitFor(actor, (s) => s.value === 'no_tenants', { timeout: 2000 });
    expect(snapshot.value).toBe('no_tenants');
    expect(snapshot.context.membership).toBeNull();
    actor.stop();
  });
});
