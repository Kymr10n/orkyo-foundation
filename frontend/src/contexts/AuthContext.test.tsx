/**
 * AuthContext BFF tests
 *
 * Verifies the BFF authentication flow via the auth machine:
 * - Init invokes /api/auth/bff/me with credentials + X-API-Key
 * - Transitions to redirecting_login when no valid session (and no URL error)
 * - Transitions to error_backend / error_network on failures
 * - Transitions to a usable stage when BFF /me returns a valid session
 *
 * The XState machine drives all transitions — these are integration tests
 * through AuthProvider.
 */

import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, waitFor, act } from "@testing-library/react";
import { AuthProvider, useAuth, debugAuth, getTenantSlugSync } from "./AuthContext";
import type { AppUser, TenantMembership } from "./AuthContext";
import { AUTH_STAGES } from "@foundation/src/constants/auth";
import { getCurrentSubdomain, consumeBreakGlassCookie } from "@foundation/src/lib/utils/tenant-navigation";

// ── Mocks ─────────────────────────────────────────────────────────────────────

vi.mock("@foundation/src/config/runtime", () => ({
  runtimeConfig: {
    apiBaseUrl: "http://localhost:5000",
    defaultTenant: "",
    baseDomain: "",
    isDev: true,
  },
}));

vi.mock("@foundation/src/lib/core/csrf", () => ({
  getCsrfToken: vi.fn(() => null),
  CSRF_HEADER_NAME: 'X-CSRF-Token',
  isMutatingMethod: vi.fn(() => false),
}));

vi.mock("@foundation/src/lib/utils/tenant-navigation", () => ({
  getCurrentSubdomain: vi.fn(() => null),
  consumeBreakGlassCookie: vi.fn(() => null),
  navigateToTenantSubdomain: vi.fn(() => false),
  redirectToLogin: vi.fn(),
  navigateToApex: vi.fn(() => false),
  getApexOrigin: vi.fn(() => "http://localhost:5173"),
}));

const mockFetch = vi.fn();
globalThis.fetch = mockFetch;

// ── Fixtures ──────────────────────────────────────────────────────────────────

const mockUser: AppUser = {
  id: "user-1",
  email: "test@example.com",
  displayName: "Test User",
  hasSeenTour: false,
};

const mockTenant: TenantMembership = {
  tenantId: "t1",
  slug: "demo",
  displayName: "Demo",
  role: "admin",
  state: "active",
  tier: "Free",
};

const mockBootstrapResponse = {
  user: mockUser,
  tosRequired: false,
  tenants: [],
};

// ── Helpers ───────────────────────────────────────────────────────────────────

function renderAuthProvider() {
  let authRef: ReturnType<typeof useAuth> | null = null;

  function Probe() {
    authRef = useAuth();
    return null;
  }

  render(
    <AuthProvider>
      <Probe />
    </AuthProvider>,
  );

  return () => authRef!;
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("AuthContext BFF session", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    localStorage.clear();
    // Ensure no URL error params by default
    Object.defineProperty(window, 'location', {
      value: { href: 'http://localhost:5173/', pathname: '/', search: '', protocol: 'http:', hostname: 'localhost' },
      writable: true,
      configurable: true,
    });
  });

  describe("init-effect BFF /me check", () => {
    it("calls BFF /me with credentials include", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => mockBootstrapResponse,
      });

      renderAuthProvider();

      await waitFor(() => expect(mockFetch).toHaveBeenCalled());

      const [url, options] = mockFetch.mock.calls[0] as [
        string,
        RequestInit,
      ];
      expect(url).toContain("/api/auth/bff/me");
      expect(options.credentials).toBe("include");
    });

    it("transitions to no_tenants when BFF session is valid but user has no tenants", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => mockBootstrapResponse,
      });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.NO_TENANTS));
      expect(getAuth().appUser?.email).toBe("test@example.com");
    });

    it("transitions to ready when BFF session has a single tenant (local dev auto-select)", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [mockTenant] }),
      });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.READY));
      expect(getAuth().membership?.slug).toBe("demo");
    });

    it("transitions to tos_required when tosRequired is true", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tosRequired: true }),
      });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.TOS_REQUIRED));
    });

    it("transitions to selecting_tenant when user has multiple tenants", async () => {
      const tenant2 = { ...mockTenant, tenantId: "t2", slug: "other" };
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [mockTenant, tenant2] }),
      });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.SELECTING_TENANT));
    });

    it("transitions to redirecting_login when BFF returns { authenticated: false }", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ authenticated: false }),
      });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.REDIRECTING_LOGIN));
      expect(getAuth().appUser).toBeNull();
    });

    it("does not expose tokens (getAccessToken returns null)", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [mockTenant] }),
      });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.READY));
      // eslint-disable-next-line @typescript-eslint/no-deprecated
      expect(getAuth().getAccessToken()).toBeNull();
      // eslint-disable-next-line @typescript-eslint/no-deprecated
      expect(getAuth().oidcUser).toBeNull();
    });
  });

  describe("failure mode separation", () => {
    it("transitions to error_backend when BFF returns 500", async () => {
      mockFetch.mockResolvedValue({ ok: false, status: 500 });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.ERROR_BACKEND));
      expect(getAuth().error).toContain("500");
    });

    it("transitions to error_backend when BFF returns 502", async () => {
      mockFetch.mockResolvedValue({ ok: false, status: 502 });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.ERROR_BACKEND));
    });

    it("transitions to error_network when fetch throws (network failure)", async () => {
      mockFetch.mockRejectedValue(new TypeError("Failed to fetch"));

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.ERROR_NETWORK));
      expect(getAuth().error).toBeTruthy();
    });

    it("transitions to redirecting_login when BFF returns 401 (no URL error)", async () => {
      mockFetch.mockResolvedValue({ ok: false, status: 401 });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.REDIRECTING_LOGIN));
    });

    it("transitions to unauthenticated with error when URL has ?error= param", async () => {
      Object.defineProperty(window, 'location', {
        value: { href: 'http://localhost:5173/?error=identity_link_failed', pathname: '/', search: '?error=identity_link_failed', protocol: 'http:', hostname: 'localhost' },
        writable: true,
        configurable: true,
      });

      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ authenticated: false }),
      });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.UNAUTHENTICATED));
      expect(getAuth().error).toContain("identity link failed");
    });
  });

  describe("derived context values", () => {
    it("sets isLoading=true during initializing", async () => {
      const getAuth = renderAuthProvider();
      // Before fetch resolves, authStage is initializing
      expect(getAuth().authStage).toBe(AUTH_STAGES.INITIALIZING);
      expect(getAuth().isLoading).toBe(true);
      await act(async () => {});
    });

    it("sets isLoading=false and isAuthenticated=true when ready", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [mockTenant] }),
      });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.READY));
      expect(getAuth().isLoading).toBe(false);
      expect(getAuth().isAuthenticated).toBe(true);
    });

    it("sets canAccessAccountPage=false during error_backend", async () => {
      mockFetch.mockResolvedValue({ ok: false, status: 500 });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.ERROR_BACKEND));
      expect(getAuth().canAccessAccountPage).toBe(false);
      expect(getAuth().canAccessAdminPage).toBe(false);
    });

    it("sets canAccessAccountPage=true when authenticated (tos_required)", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tosRequired: true }),
      });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.TOS_REQUIRED));
      expect(getAuth().canAccessAccountPage).toBe(true);
    });

    it("sets canAccessAdminPage=false for non-admin users", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, isSiteAdmin: false }),
      });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.NO_TENANTS));
      expect(getAuth().canAccessAdminPage).toBe(false);
    });

    it("sets canAccessAdminPage=true for site admin in no_tenants_admin", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [], isSiteAdmin: true }),
      });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.NO_TENANTS_ADMIN));
      expect(getAuth().canAccessAdminPage).toBe(true);
      expect(getAuth().canAccessAccountPage).toBe(true);
    });
  });

  // ── Tenant switching ──────────────────────────────────────────────────────

  describe("switchTenant", () => {
    it("transitions from ready back to selecting_tenant (local dev, multi-tenant)", async () => {
      const tenant2: TenantMembership = { ...mockTenant, tenantId: "t2", slug: "other" };
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [mockTenant, tenant2] }),
      });

      const getAuth = renderAuthProvider();

      // Wait for machine to settle on selecting_tenant first (multi-tenant + no subdomain)
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.SELECTING_TENANT));

      // Select a tenant — machine goes to redirecting_to_tenant → ready (local dev)
      act(() => {
        getAuth().send({ type: 'TENANT_SELECTED', membership: mockTenant });
      });
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.READY));
      expect(getAuth().membership?.slug).toBe("demo");

      // Switch back to the selector
      act(() => { getAuth().switchTenant(); });
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.SELECTING_TENANT));
      expect(getAuth().membership).toBeNull();
    });

    it("clears localStorage when switchTenant is called", async () => {
      const tenant2: TenantMembership = { ...mockTenant, tenantId: "t2", slug: "other" };
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [mockTenant, tenant2] }),
      });

      const getAuth = renderAuthProvider();
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.SELECTING_TENANT));

      act(() => { getAuth().send({ type: 'TENANT_SELECTED', membership: mockTenant }); });
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.READY));

      act(() => { getAuth().switchTenant(); });
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.SELECTING_TENANT));
      expect(localStorage.getItem('active_membership')).toBeNull();
      expect(localStorage.getItem('tenant_slug')).toBeNull();
    });

    it("sessionData tenants are still available after switchTenant for re-selection", async () => {
      const tenant2: TenantMembership = { ...mockTenant, tenantId: "t2", slug: "other" };
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [mockTenant, tenant2] }),
      });

      const getAuth = renderAuthProvider();
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.SELECTING_TENANT));
      act(() => { getAuth().send({ type: 'TENANT_SELECTED', membership: mockTenant }); });
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.READY));

      act(() => { getAuth().switchTenant(); });
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.SELECTING_TENANT));
      // Session data is preserved — the selector can re-render the tenant list
      expect(getAuth().sessionData?.tenants).toHaveLength(2);
    });
  });

  // ── Break-glass / subdomain scenarios ─────────────────────────────────────
  //
  // These tests exercise the subdomain path in fetchSessionFromBff — the code
  // path used when a site admin enters a tenant via the admin panel.
  // getCurrentSubdomain() is overridden per-test to simulate the tenant subdomain.

  describe("break-glass entry (tenant subdomain)", () => {
    beforeEach(() => {
      // Simulate being on demo.orkyo.com with a break-glass cookie present.
      vi.mocked(getCurrentSubdomain).mockReturnValue("demo");
      vi.mocked(consumeBreakGlassCookie).mockReturnValue({ sessionId: "bg-session-123", tenantId: "bg-tenant-id" });
    });

    it("transitions to ready when site admin has no natural tenants (regression: was routing to no_tenants_admin)", async () => {
      // Root cause of the bug: noTenantsAdmin guard was evaluated before
      // membershipResolved, so a synthesised break-glass membership was ignored.
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [], isSiteAdmin: true }),
      });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.READY));
      expect(getAuth().membership?.slug).toBe("demo");
      expect(getAuth().membership?.isBreakGlass).toBe(true);
      expect(getAuth().membership?.breakGlassSessionId).toBe("bg-session-123");
    });

    it("transitions to ready when site admin is not a natural member of the target tenant", async () => {
      // Admin has one natural tenant ("other") but is entering "demo" via break-glass.
      const otherTenant: TenantMembership = { ...mockTenant, slug: "other", tenantId: "t2" };
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [otherTenant], isSiteAdmin: true }),
      });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.READY));
      expect(getAuth().membership?.slug).toBe("demo");
      expect(getAuth().membership?.isBreakGlass).toBe(true);
    });

    it("upgrades a natural membership with break-glass flag when admin is already a member", async () => {
      // Admin is a natural member of "demo" and enters via break-glass.
      // The membership should carry isBreakGlass: true for audit purposes.
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [mockTenant], isSiteAdmin: true }),
      });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.READY));
      expect(getAuth().membership?.slug).toBe("demo");
      expect(getAuth().membership?.isBreakGlass).toBe(true);
      expect(getAuth().membership?.breakGlassSessionId).toBe("bg-session-123");
    });

    it("does not grant break-glass membership to non-admin users with no matching tenant", async () => {
      // A regular user on a subdomain they don't belong to should not get a membership.
      // The machine falls through to redirecting_to_tenant (→ ready in local dev via
      // isLocalDev guard), but critically membership must remain null.
      vi.mocked(consumeBreakGlassCookie).mockReturnValue(null);
      const otherTenant: TenantMembership = { ...mockTenant, slug: "other", tenantId: "t2" };
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [otherTenant], isSiteAdmin: false }),
      });

      const getAuth = renderAuthProvider();

      await waitFor(() => expect(getAuth().authStage).not.toBe(AUTH_STAGES.INITIALIZING));
      expect(getAuth().membership).toBeNull();
    });
  });

  // ── Logout flow ─────────────────────────────────────────────────────────

  describe("logout", () => {
    it("navigates to BFF logout endpoint (GET, no POST fetch)", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [mockTenant] }),
      });

      const getAuth = renderAuthProvider();
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.READY));

      // Clear fetch history so we can assert no POST was made during logout
      mockFetch.mockClear();

      act(() => { getAuth().logout(); });

      // The machine should navigate to the BFF logout URL (GET-based redirect)
      await waitFor(() =>
        expect(window.location.href).toContain("/api/auth/bff/logout?returnTo="),
      );

      // Must NOT have made a POST fetch to /logout — the backend rejects it with 405
      const logoutFetches = mockFetch.mock.calls.filter(
        (call) => typeof call[0] === "string" && call[0].includes("/logout"),
      );
      expect(logoutFetches).toHaveLength(0);
    });

    it("clears session and storage before navigating", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [mockTenant] }),
      });

      const getAuth = renderAuthProvider();
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.READY));
      expect(getAuth().appUser).not.toBeNull();
      expect(localStorage.getItem('active_membership')).not.toBeNull();

      act(() => { getAuth().logout(); });

      await waitFor(() => expect(getAuth().appUser).toBeNull());
      expect(getAuth().membership).toBeNull();
      expect(localStorage.getItem('active_membership')).toBeNull();
      expect(localStorage.getItem('tenant_slug')).toBeNull();
    });
  });

  // ── Actions: login, refresh, clearMembership, setAppUser ──────────────────

  describe("actions", () => {
    it("login() redirects to BFF login endpoint", async () => {
      // Start from unauthenticated (URL error param) so LOGIN event is accepted
      Object.defineProperty(window, 'location', {
        value: { href: 'http://localhost:5173/?error=test', pathname: '/', search: '?error=test', protocol: 'http:', hostname: 'localhost' },
        writable: true,
        configurable: true,
      });
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ authenticated: false }),
      });

      const getAuth = renderAuthProvider();
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.UNAUTHENTICATED));

      act(() => { getAuth().login("/dashboard"); });

      await waitFor(() =>
        expect(window.location.href).toContain("/api/auth/bff/login"),
      );
    });

    it("refresh() re-invokes the bootstrap flow", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [mockTenant] }),
      });

      const getAuth = renderAuthProvider();
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.READY));

      mockFetch.mockClear();
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [mockTenant] }),
      });

      act(() => { getAuth().refresh(); });

      await waitFor(() => expect(mockFetch).toHaveBeenCalled());
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.READY));
    });

    it("clearMembership() removes the active membership", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [mockTenant] }),
      });

      const getAuth = renderAuthProvider();
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.READY));
      expect(getAuth().membership).not.toBeNull();

      act(() => { getAuth().clearMembership(); });
      await waitFor(() => expect(getAuth().membership).toBeNull());
    });

    it("setAppUser() updates the app user", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [mockTenant] }),
      });

      const getAuth = renderAuthProvider();
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.READY));
      expect(getAuth().appUser?.displayName).toBe("Test User");

      const updated: AppUser = { ...mockUser, displayName: "Updated Name" };
      act(() => { getAuth().setAppUser(updated); });
      await waitFor(() => expect(getAuth().appUser?.displayName).toBe("Updated Name"));
    });

    it("setMembership() updates the active membership", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [mockTenant] }),
      });

      const getAuth = renderAuthProvider();
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.READY));
      expect(getAuth().membership?.slug).toBe("demo");

      const upgraded: TenantMembership = { ...mockTenant, role: "owner" };
      act(() => { getAuth().setMembership(upgraded); });
      await waitFor(() => expect(getAuth().membership?.role).toBe("owner"));
    });
  });

  // ── Derived: tenantSlug ─────────────────────────────────────────────────

  describe("tenantSlug", () => {
    it("returns the slug of the active membership", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => ({ ...mockBootstrapResponse, tenants: [mockTenant] }),
      });

      const getAuth = renderAuthProvider();
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.READY));
      expect(getAuth().tenantSlug).toBe("demo");
    });

    it("returns null when no membership is set", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        status: 200,
        json: async () => mockBootstrapResponse,
      });

      const getAuth = renderAuthProvider();
      await waitFor(() => expect(getAuth().authStage).toBe(AUTH_STAGES.NO_TENANTS));
      expect(getAuth().tenantSlug).toBeNull();
    });
  });
});

// ── useAuth outside provider ────────────────────────────────────────────────

describe("useAuth outside AuthProvider", () => {
  it("throws when used outside AuthProvider", () => {
    const consoleSpy = vi.spyOn(console, "error").mockImplementation(() => {});
    function Orphan() {
      useAuth();
      return null;
    }
    expect(() => render(<Orphan />)).toThrow("useAuth must be used within an AuthProvider");
    consoleSpy.mockRestore();
  });
});

// ── getTenantSlugSync ─────────────────────────────────────────────────────

describe("getTenantSlugSync", () => {
  beforeEach(() => localStorage.clear());

  it("returns null when localStorage has no tenant slug", () => {
    expect(getTenantSlugSync()).toBeNull();
  });

  it("returns the stored slug", () => {
    localStorage.setItem("tenant_slug", "acme");
    expect(getTenantSlugSync()).toBe("acme");
  });
});

// ── debugAuth ─────────────────────────────────────────────────────────────

describe("debugAuth", () => {
  it("logs debug info in dev mode", () => {
    const logSpy = vi.spyOn(console, "log").mockImplementation(() => {});
    debugAuth("test-context");
    expect(logSpy).toHaveBeenCalledWith(
      "[DEBUG]",
      "[Auth Debug: test-context]",
      expect.objectContaining({ pathname: expect.any(String) }),
    );
    logSpy.mockRestore();
  });
});
