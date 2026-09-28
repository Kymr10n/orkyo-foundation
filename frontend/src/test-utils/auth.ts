/**
 * Typed `useAuth()` values for tests. Every field of the real context is present, so a mock
 * cannot drift from the shape components read; override only what the test is about.
 *
 *   vi.mock('@foundation/src/contexts/AuthContext', async (importOriginal) => ({
 *     ...(await importOriginal<object>()),
 *     useAuth: vi.fn(),
 *   }));
 *   vi.mocked(useAuth).mockReturnValue(mockAuth({ role: 'viewer' }));
 */
import { vi } from 'vitest';
import type {
  AppUser,
  SessionBootstrapResponse,
  TenantMembership,
  useAuth,
} from '@foundation/src/contexts/AuthContext';
import { AUTH_STAGES } from '@foundation/src/constants/auth';

export type AuthValue = ReturnType<typeof useAuth>;

/** Session fields to override; each tenant is completed with {@link mockMembership}. */
export type MockSessionOptions = Partial<Omit<SessionBootstrapResponse, 'tenants'>> & {
  tenants?: Partial<TenantMembership>[];
};

export interface MockAuthOptions
  extends Partial<Omit<AuthValue, 'membership' | 'appUser' | 'sessionData'>> {
  /** Shorthand for `membership.role`. */
  role?: string;
  /** Shorthand for `membership.isTenantAdmin`. */
  isTenantAdmin?: boolean;
  /** Fields over the default membership; `null` for no active tenant. */
  membership?: Partial<TenantMembership> | null;
  /** Fields over the default user; `null` for no user. */
  appUser?: Partial<AppUser> | null;
  /** A bootstrap response built by {@link mockSessionData}; `null` (the default) for none. */
  sessionData?: MockSessionOptions | null;
}

/** A complete tenant membership: an active admin of tenant "acme". */
export function mockMembership(overrides: Partial<TenantMembership> = {}): TenantMembership {
  return {
    tenantId: 'tenant-1',
    slug: 'acme',
    displayName: 'Acme',
    role: 'admin',
    state: 'active',
    isTenantAdmin: true,
    ...overrides,
  };
}

/** A complete signed-in user who has seen the tour. */
export function mockAppUser(overrides: Partial<AppUser> = {}): AppUser {
  return {
    id: 'user-1',
    email: 'user@example.com',
    displayName: 'Test User',
    hasSeenTour: true,
    ...overrides,
  };
}

/** A complete session bootstrap response: no ToS pending, no tenants unless given. */
export function mockSessionData({ tenants = [], ...overrides }: MockSessionOptions = {}): SessionBootstrapResponse {
  return {
    user: mockAppUser(),
    tosRequired: false,
    tenants: tenants.map((t) => mockMembership(t)),
    ...overrides,
  };
}

/** A complete `useAuth()` value: a signed-in tenant admin at the `ready` stage. */
export function mockAuth(options: MockAuthOptions = {}): AuthValue {
  const { role, isTenantAdmin, membership, appUser, sessionData, ...rest } = options;
  const resolvedMembership =
    membership === null
      ? null
      : mockMembership({
          ...(role !== undefined && { role }),
          ...(isTenantAdmin !== undefined && { isTenantAdmin }),
          ...membership,
        });
  return {
    authStage: AUTH_STAGES.READY,
    appUser: appUser === null ? null : mockAppUser(appUser),
    membership: resolvedMembership,
    sessionData: sessionData ? mockSessionData(sessionData) : null,
    isSiteAdmin: false,
    error: null,
    isAuthenticated: true,
    isLoading: false,
    tenantSlug: resolvedMembership?.slug ?? null,
    canAccessAccountPage: true,
    canAccessAdminPage: false,
    login: vi.fn(),
    logout: vi.fn(),
    refresh: vi.fn(),
    setMembership: vi.fn(),
    clearMembership: vi.fn(),
    switchTenant: vi.fn(),
    setAppUser: vi.fn(),
    send: vi.fn(),
    ...rest,
  };
}
