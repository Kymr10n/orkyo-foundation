import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as ApiUtils from "../core/api-utils";
import {
  getAdminTenants,
  createAdminTenant,
  updateAdminTenant,
  updateAdminTenantTier,
  deleteAdminTenant,
  getAdminUsers,
  getAdminUser,
  deactivateAdminUser,
  reactivateAdminUser,
  deleteAdminUser,
  promoteSiteAdmin,
  revokeSiteAdmin,
  getAdminTenantMembers,
  addAdminTenantMember,
  updateAdminTenantMember,
  removeAdminTenantMember,
  auditBreakGlassEntry,
  auditBreakGlassExit,
  renewBreakGlassSession,
  getBreakGlassSessionStatus,
} from "./admin-api";

vi.mock("@foundation/src/contexts/AuthContext", () => ({
  getAuthTokenSync: () => null,
  getTenantSlugSync: () => null,
}));

vi.mock("@foundation/src/lib/core/csrf", () => ({
  getCsrfToken: () => "test-csrf-token",
  CSRF_HEADER_NAME: "X-CSRF-Token",
  isMutatingMethod: (m: string) =>
    ["POST", "PUT", "PATCH", "DELETE"].includes(m.toUpperCase()),
}));

vi.mock("@foundation/src/config/runtime", () => ({
  runtimeConfig: { apiBaseUrl: "http://localhost:5000", baseDomain: "" },
}));

vi.mock("../core/api-utils", async (importOriginal) => {
  const actual = await importOriginal<typeof ApiUtils>();
  return {
    ...actual,
    handleApiError: vi.fn().mockImplementation(async (response: Response) => {
      const text =
        (await response.text?.()) || `Error ${(response).status}`;
      throw new Error(text);
    }),
    API_BASE_URL: "http://localhost:5000",
  };
});

const mockFetch = vi.fn();
global.fetch = mockFetch;

// ============================================================================
// Shared fixtures
// ============================================================================

const TENANT_ID = "tenant-456";
const USER_ID = "user-123";

const mockTenant = {
  id: TENANT_ID,
  slug: "acme",
  displayName: "Acme Corp",
  status: "active",
  dbIdentifier: "acme_db",
  createdAt: "2025-01-01T00:00:00Z",
  updatedAt: "2025-01-02T00:00:00Z",
  memberCount: 5,
  tier: "Free",
};

const mockUser = {
  id: USER_ID,
  email: "test@example.com",
  displayName: "Test User",
  status: "active",
  createdAt: "2025-01-01T00:00:00Z",
  updatedAt: "2025-01-02T00:00:00Z",
  lastLoginAt: "2025-03-01T10:00:00Z",
  membershipCount: 2,
  identityCount: 1,
  isSiteAdmin: false,
  ownedTenantId: TENANT_ID,
  ownedTenantTier: "Free",
};

const mockMember = {
  userId: USER_ID,
  email: "test@example.com",
  displayName: "Test User",
  userStatus: "active",
  role: "editor",
  membershipStatus: "active",
  joinedAt: "2025-01-05T00:00:00Z",
};

// ============================================================================
// Tenant Management
// ============================================================================

describe("admin-api — Tenant Management", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // --------------------------------------------------------------------------
  // getAdminTenants
  // --------------------------------------------------------------------------

  describe("getAdminTenants", () => {
    it("sends GET request to /api/admin/tenants", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ tenants: [mockTenant] }),
      });

      await getAdminTenants();

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/admin/tenants",
        expect.objectContaining({
          method: "GET",
          credentials: "include",
        }),
      );
    });

    it("returns the tenants array from the response", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ tenants: [mockTenant] }),
      });

      const result = await getAdminTenants();

      expect(result).toEqual({ tenants: [mockTenant] });
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 403,
        text: async () => "Forbidden",
      });

      await expect(getAdminTenants()).rejects.toThrow("Forbidden");
    });
  });

  // --------------------------------------------------------------------------
  // createAdminTenant
  // --------------------------------------------------------------------------

  describe("createAdminTenant", () => {
    const payload = { slug: "acme", displayName: "Acme Corp" };

    it("sends POST to /api/admin/tenants with body", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => mockTenant,
      });

      await createAdminTenant(payload);

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/admin/tenants",
        expect.objectContaining({
          method: "POST",
          credentials: "include",
          body: JSON.stringify(payload),
        }),
      );
    });

    it("returns the created tenant", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => mockTenant,
      });

      const result = await createAdminTenant(payload);

      expect(result).toEqual(mockTenant);
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 409,
        text: async () => "Slug already exists",
      });

      await expect(createAdminTenant(payload)).rejects.toThrow(
        "Slug already exists",
      );
    });
  });

  // --------------------------------------------------------------------------
  // updateAdminTenant
  // --------------------------------------------------------------------------

  describe("updateAdminTenant", () => {
    const payload = { status: "suspended" };

    it("sends PATCH to /api/admin/tenants/{tenantId} with body", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ message: "Tenant updated" }),
      });

      await updateAdminTenant(TENANT_ID, payload);

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5000/api/admin/tenants/${TENANT_ID}`,
        expect.objectContaining({
          method: "PATCH",
          credentials: "include",
          body: JSON.stringify(payload),
        }),
      );
    });

    it("returns message on success", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ message: "Tenant updated" }),
      });

      const result = await updateAdminTenant(TENANT_ID, payload);

      expect(result).toEqual({ message: "Tenant updated" });
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 404,
        text: async () => "Tenant not found",
      });

      await expect(updateAdminTenant(TENANT_ID, payload)).rejects.toThrow(
        "Tenant not found",
      );
    });
  });

  // --------------------------------------------------------------------------
  // deleteAdminTenant
  // --------------------------------------------------------------------------

  describe("deleteAdminTenant", () => {
    it("sends DELETE to /api/admin/tenants/{tenantId}", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      await deleteAdminTenant(TENANT_ID);

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5000/api/admin/tenants/${TENANT_ID}`,
        expect.objectContaining({
          method: "DELETE",
          credentials: "include",
        }),
      );
    });

    it("resolves to void on success", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      const result = await deleteAdminTenant(TENANT_ID);

      expect(result).toBeUndefined();
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 404,
        text: async () => "Tenant not found",
      });

      await expect(deleteAdminTenant(TENANT_ID)).rejects.toThrow(
        "Tenant not found",
      );
    });
  });
  // --------------------------------------------------------------------------
  // updateAdminTenantTier
  // --------------------------------------------------------------------------

  describe("updateAdminTenantTier", () => {
    it("sends PATCH to /api/admin/tenants/{tenantId}/tier with body", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ message: "Tenant tier updated", tier: "Professional" }),
      });

      await updateAdminTenantTier(TENANT_ID, "Professional");

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5000/api/admin/tenants/${TENANT_ID}/tier`,
        expect.objectContaining({
          method: "PATCH",
          credentials: "include",
          body: JSON.stringify({ tier: "Professional" }),
        }),
      );
    });

    it("returns message and tier on success", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ message: "Tenant tier updated", tier: "Enterprise" }),
      });

      const result = await updateAdminTenantTier(TENANT_ID, "Enterprise");

      expect(result).toEqual({ message: "Tenant tier updated", tier: "Enterprise" });
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 409,
        text: async () => "Cannot downgrade: tenant has 10 active members but Free tier allows 5",
      });

      await expect(updateAdminTenantTier(TENANT_ID, "Free")).rejects.toThrow(
        "Cannot downgrade",
      );
    });
  });
});

// ============================================================================
// User Management
// ============================================================================

describe("admin-api — User Management", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // --------------------------------------------------------------------------
  // getAdminUsers
  // --------------------------------------------------------------------------

  describe("getAdminUsers", () => {
    it("sends GET to /api/admin/users with no params when called without arguments", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ users: [mockUser] }),
      });

      await getAdminUsers();

      const [url] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toBe("http://localhost:5000/api/admin/users");
      expect(mockFetch).toHaveBeenCalledWith(
        expect.any(String),
        expect.objectContaining({ method: "GET", credentials: "include" }),
      );
    });

    it("appends ?search= when a search term is provided", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ users: [mockUser] }),
      });

      await getAdminUsers("test@example.com");

      const [url] = mockFetch.mock.calls[0] as [string, RequestInit];
      expect(url).toContain("search=test%40example.com");
    });

    it("returns the users array from the response", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ users: [mockUser] }),
      });

      const result = await getAdminUsers();

      expect(result).toEqual({ users: [mockUser] });
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 403,
        text: async () => "Forbidden",
      });

      await expect(getAdminUsers()).rejects.toThrow("Forbidden");
    });
  });

  // --------------------------------------------------------------------------
  // getAdminUser
  // --------------------------------------------------------------------------

  describe("getAdminUser", () => {
    const mockUserDetail = {
      ...mockUser,
      identities: [],
      memberships: [],
    };

    it("sends GET to /api/admin/users/{userId}", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => mockUserDetail,
      });

      await getAdminUser(USER_ID);

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5000/api/admin/users/${USER_ID}`,
        expect.objectContaining({ method: "GET", credentials: "include" }),
      );
    });

    it("returns the user detail object", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => mockUserDetail,
      });

      const result = await getAdminUser(USER_ID);

      expect(result).toEqual(mockUserDetail);
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 404,
        text: async () => "User not found",
      });

      await expect(getAdminUser(USER_ID)).rejects.toThrow("User not found");
    });
  });

  // --------------------------------------------------------------------------
  // deactivateAdminUser
  // --------------------------------------------------------------------------

  describe("deactivateAdminUser", () => {
    it("sends POST to /api/admin/users/{userId}/deactivate", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      await deactivateAdminUser(USER_ID);

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5000/api/admin/users/${USER_ID}/deactivate`,
        expect.objectContaining({
          method: "POST",
          credentials: "include",
        }),
      );
    });

    it("resolves to void on success", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      const result = await deactivateAdminUser(USER_ID);

      expect(result).toBeUndefined();
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 404,
        text: async () => "User not found",
      });

      await expect(deactivateAdminUser(USER_ID)).rejects.toThrow(
        "User not found",
      );
    });
  });

  // --------------------------------------------------------------------------
  // reactivateAdminUser
  // --------------------------------------------------------------------------

  describe("reactivateAdminUser", () => {
    it("sends POST to /api/admin/users/{userId}/reactivate", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      await reactivateAdminUser(USER_ID);

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5000/api/admin/users/${USER_ID}/reactivate`,
        expect.objectContaining({
          method: "POST",
          credentials: "include",
        }),
      );
    });

    it("resolves to void on success", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      const result = await reactivateAdminUser(USER_ID);

      expect(result).toBeUndefined();
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 400,
        text: async () => "User already active",
      });

      await expect(reactivateAdminUser(USER_ID)).rejects.toThrow(
        "User already active",
      );
    });
  });

  // --------------------------------------------------------------------------
  // deleteAdminUser
  // --------------------------------------------------------------------------

  describe("deleteAdminUser", () => {
    it("sends DELETE to /api/admin/users/{userId}", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      await deleteAdminUser(USER_ID);

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5000/api/admin/users/${USER_ID}`,
        expect.objectContaining({
          method: "DELETE",
          credentials: "include",
        }),
      );
    });

    it("resolves to void on success", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      const result = await deleteAdminUser(USER_ID);

      expect(result).toBeUndefined();
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 404,
        text: async () => "User not found",
      });

      await expect(deleteAdminUser(USER_ID)).rejects.toThrow("User not found");
    });
  });

  // --------------------------------------------------------------------------
  // promoteSiteAdmin
  // --------------------------------------------------------------------------

  describe("promoteSiteAdmin", () => {
    it("sends POST to /api/admin/users/{userId}/promote-site-admin", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      await promoteSiteAdmin(USER_ID);

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5000/api/admin/users/${USER_ID}/promote-site-admin`,
        expect.objectContaining({
          method: "POST",
          credentials: "include",
        }),
      );
    });

    it("resolves to void on success", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      const result = await promoteSiteAdmin(USER_ID);

      expect(result).toBeUndefined();
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 403,
        text: async () => "Forbidden",
      });

      await expect(promoteSiteAdmin(USER_ID)).rejects.toThrow("Forbidden");
    });
  });

  // --------------------------------------------------------------------------
  // revokeSiteAdmin
  // --------------------------------------------------------------------------

  describe("revokeSiteAdmin", () => {
    it("sends POST to /api/admin/users/{userId}/revoke-site-admin", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      await revokeSiteAdmin(USER_ID);

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5000/api/admin/users/${USER_ID}/revoke-site-admin`,
        expect.objectContaining({
          method: "POST",
          credentials: "include",
        }),
      );
    });

    it("resolves to void on success", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      const result = await revokeSiteAdmin(USER_ID);

      expect(result).toBeUndefined();
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 403,
        text: async () => "Forbidden",
      });

      await expect(revokeSiteAdmin(USER_ID)).rejects.toThrow("Forbidden");
    });
  });
});

// ============================================================================
// Membership Management
// ============================================================================

describe("admin-api — Membership Management", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // --------------------------------------------------------------------------
  // getAdminTenantMembers
  // --------------------------------------------------------------------------

  describe("getAdminTenantMembers", () => {
    it("sends GET to /api/admin/tenants/{tenantId}/members", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({
          tenantId: TENANT_ID,
          tenantSlug: "acme",
          members: [mockMember],
        }),
      });

      await getAdminTenantMembers(TENANT_ID);

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5000/api/admin/tenants/${TENANT_ID}/members`,
        expect.objectContaining({ method: "GET", credentials: "include" }),
      );
    });

    it("returns tenantId, tenantSlug and members array", async () => {
      const responseBody = {
        tenantId: TENANT_ID,
        tenantSlug: "acme",
        members: [mockMember],
      };
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => responseBody,
      });

      const result = await getAdminTenantMembers(TENANT_ID);

      expect(result).toEqual(responseBody);
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 404,
        text: async () => "Tenant not found",
      });

      await expect(getAdminTenantMembers(TENANT_ID)).rejects.toThrow(
        "Tenant not found",
      );
    });
  });

  // --------------------------------------------------------------------------
  // addAdminTenantMember
  // --------------------------------------------------------------------------

  describe("addAdminTenantMember", () => {
    const payload = { userId: USER_ID, role: "editor" };

    it("sends POST to /api/admin/tenants/{tenantId}/members with body", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({
          userId: USER_ID,
          tenantId: TENANT_ID,
          role: "editor",
          status: "active",
        }),
      });

      await addAdminTenantMember(TENANT_ID, payload);

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5000/api/admin/tenants/${TENANT_ID}/members`,
        expect.objectContaining({
          method: "POST",
          credentials: "include",
          body: JSON.stringify(payload),
        }),
      );
    });

    it("returns the new membership record", async () => {
      const responseBody = {
        userId: USER_ID,
        tenantId: TENANT_ID,
        role: "editor",
        status: "active",
      };
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => responseBody,
      });

      const result = await addAdminTenantMember(TENANT_ID, payload);

      expect(result).toEqual(responseBody);
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 409,
        text: async () => "User is already a member",
      });

      await expect(addAdminTenantMember(TENANT_ID, payload)).rejects.toThrow(
        "User is already a member",
      );
    });
  });

  // --------------------------------------------------------------------------
  // updateAdminTenantMember
  // --------------------------------------------------------------------------

  describe("updateAdminTenantMember", () => {
    const payload = { role: "admin" };

    it("sends PATCH to /api/admin/tenants/{tenantId}/members/{userId} with body", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({
          userId: USER_ID,
          tenantId: TENANT_ID,
          role: "admin",
          status: "active",
        }),
      });

      await updateAdminTenantMember(TENANT_ID, USER_ID, payload);

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5000/api/admin/tenants/${TENANT_ID}/members/${USER_ID}`,
        expect.objectContaining({
          method: "PATCH",
          credentials: "include",
          body: JSON.stringify(payload),
        }),
      );
    });

    it("returns the updated membership record", async () => {
      const responseBody = {
        userId: USER_ID,
        tenantId: TENANT_ID,
        role: "admin",
        status: "active",
      };
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => responseBody,
      });

      const result = await updateAdminTenantMember(TENANT_ID, USER_ID, payload);

      expect(result).toEqual(responseBody);
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 404,
        text: async () => "Membership not found",
      });

      await expect(
        updateAdminTenantMember(TENANT_ID, USER_ID, payload),
      ).rejects.toThrow("Membership not found");
    });
  });

  // --------------------------------------------------------------------------
  // removeAdminTenantMember
  // --------------------------------------------------------------------------

  describe("removeAdminTenantMember", () => {
    it("sends DELETE to /api/admin/tenants/{tenantId}/members/{userId}", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      await removeAdminTenantMember(TENANT_ID, USER_ID);

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5000/api/admin/tenants/${TENANT_ID}/members/${USER_ID}`,
        expect.objectContaining({
          method: "DELETE",
          credentials: "include",
        }),
      );
    });

    it("resolves to void on success", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      const result = await removeAdminTenantMember(TENANT_ID, USER_ID);

      expect(result).toBeUndefined();
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 404,
        text: async () => "Membership not found",
      });

      await expect(
        removeAdminTenantMember(TENANT_ID, USER_ID),
      ).rejects.toThrow("Membership not found");
    });
  });
});

// ============================================================================
// Break Glass
// ============================================================================

describe("admin-api — Break Glass", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  // --------------------------------------------------------------------------
  // auditBreakGlassEntry
  // --------------------------------------------------------------------------

  describe("auditBreakGlassEntry", () => {
    const SESSION_ID = "session-abc-123";

    it("sends POST to /api/admin/break-glass/entry with tenantSlug and reason", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ success: true, sessionId: SESSION_ID }),
      });

      await auditBreakGlassEntry("acme", "Support investigation");

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/admin/break-glass/entry",
        expect.objectContaining({
          method: "POST",
          credentials: "include",
          body: JSON.stringify({
            tenantSlug: "acme",
            reason: "Support investigation",
          }),
        }),
      );
    });

    it("returns the session metadata from the response", async () => {
      const expiresAt = "2026-04-18T13:00:00Z";
      const createdAt = "2026-04-18T12:00:00Z";
      const absoluteExpiresAt = "2026-04-18T20:00:00Z";
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({
          success: true,
          sessionId: SESSION_ID,
          createdAt,
          expiresAt,
          absoluteExpiresAt,
        }),
      });

      const result = await auditBreakGlassEntry("acme", "Support investigation");

      expect(result.sessionId).toBe(SESSION_ID);
      expect(result.createdAt).toBe(createdAt);
      expect(result.expiresAt).toBe(expiresAt);
      expect(result.absoluteExpiresAt).toBe(absoluteExpiresAt);
    });

    it("sends POST with undefined reason when none is provided", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ success: true, sessionId: SESSION_ID }),
      });

      await auditBreakGlassEntry("acme");

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/admin/break-glass/entry",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({ tenantSlug: "acme", reason: undefined }),
        }),
      );
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 403,
        text: async () => "Forbidden",
      });

      await expect(
        auditBreakGlassEntry("acme", "Support investigation"),
      ).rejects.toThrow("Forbidden");
    });
  });

  // --------------------------------------------------------------------------
  // auditBreakGlassExit
  // --------------------------------------------------------------------------

  describe("auditBreakGlassExit", () => {
    const SESSION_ID = "session-abc-123";

    it("sends POST to /api/admin/break-glass/exit with sessionId", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ success: true }),
      });

      await auditBreakGlassExit(SESSION_ID);

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/admin/break-glass/exit",
        expect.objectContaining({
          method: "POST",
          credentials: "include",
          body: JSON.stringify({ sessionId: SESSION_ID }),
        }),
      );
    });

    it("resolves to void on success", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ success: true }),
      });

      const result = await auditBreakGlassExit(SESSION_ID);

      expect(result).toBeUndefined();
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 404,
        text: async () => "Session not found",
      });

      await expect(auditBreakGlassExit(SESSION_ID)).rejects.toThrow(
        "Session not found",
      );
    });
  });

  // --------------------------------------------------------------------------
  // renewBreakGlassSession
  // --------------------------------------------------------------------------

  describe("renewBreakGlassSession", () => {
    const SESSION_ID = "session-renew-123";

    it("sends POST to /api/admin/break-glass/renew with sessionId", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({
          sessionId: SESSION_ID,
          createdAt: "2026-04-18T12:00:00Z",
          expiresAt: "2026-04-18T14:00:00Z",
          absoluteExpiresAt: "2026-04-18T20:00:00Z",
        }),
      });

      await renewBreakGlassSession(SESSION_ID);

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/admin/break-glass/renew",
        expect.objectContaining({
          method: "POST",
          credentials: "include",
          body: JSON.stringify({ sessionId: SESSION_ID }),
        }),
      );
    });

    it("returns renewed session metadata", async () => {
      const expected = {
        sessionId: SESSION_ID,
        createdAt: "2026-04-18T12:00:00Z",
        expiresAt: "2026-04-18T14:00:00Z",
        absoluteExpiresAt: "2026-04-18T20:00:00Z",
      };
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => expected,
      });

      const result = await renewBreakGlassSession(SESSION_ID);

      expect(result.sessionId).toBe(SESSION_ID);
      expect(result.expiresAt).toBe(expected.expiresAt);
      expect(result.absoluteExpiresAt).toBe(expected.absoluteExpiresAt);
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 410,
        statusText: "Gone",
        json: async () => ({ error: "Hard cap reached" }),
      });

      await expect(renewBreakGlassSession(SESSION_ID)).rejects.toThrow();
    });
  });

  // --------------------------------------------------------------------------
  // getBreakGlassSessionStatus
  // --------------------------------------------------------------------------

  describe("getBreakGlassSessionStatus", () => {
    it("sends GET to /api/admin/break-glass/session/{tenantSlug}", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({
          sessionId: "abc",
          tenantSlug: "acme",
          createdAt: "2026-04-18T12:00:00Z",
          expiresAt: "2026-04-18T13:00:00Z",
          absoluteExpiresAt: "2026-04-18T20:00:00Z",
        }),
      });

      await getBreakGlassSessionStatus("acme");

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/admin/break-glass/session/acme",
        expect.objectContaining({ method: "GET", credentials: "include" }),
      );
    });

    it("returns session status on success", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({
          sessionId: "abc",
          tenantSlug: "acme",
          createdAt: "2026-04-18T12:00:00Z",
          expiresAt: "2026-04-18T13:00:00Z",
          absoluteExpiresAt: "2026-04-18T20:00:00Z",
        }),
      });

      const result = await getBreakGlassSessionStatus("acme");

      expect(result).not.toBeNull();
      expect(result!.sessionId).toBe("abc");
      expect(result!.tenantSlug).toBe("acme");
    });

    it("returns null when no active session (404)", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 404,
        statusText: "Not Found",
        json: async () => ({ error: "No session", code: "break_glass_expired" }),
      });

      const result = await getBreakGlassSessionStatus("acme");

      expect(result).toBeNull();
    });

    it("encodes special characters in tenantSlug", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({
          sessionId: "abc",
          tenantSlug: "my tenant",
          createdAt: "2026-04-18T12:00:00Z",
          expiresAt: "2026-04-18T13:00:00Z",
          absoluteExpiresAt: "2026-04-18T20:00:00Z",
        }),
      });

      await getBreakGlassSessionStatus("my tenant");

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/admin/break-glass/session/my%20tenant",
        expect.anything(),
      );
    });
  });
});
