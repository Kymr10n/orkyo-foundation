import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as ApiUtils from "../core/api-utils";
import {
  canCreateTenant,
  cancelTenantDeletion,
  createTenant,
  deleteTenant,
  getStarterTemplates,
  getTenantMemberships,
  leaveTenant,
} from "./tenant-account-api";

// Mock AuthContext (BFF mode)
vi.mock("@foundation/src/contexts/AuthContext", () => ({
  getAuthTokenSync: () => null,
  getTenantSlugSync: () => null,
}));

// Mock CSRF
vi.mock("@foundation/src/lib/core/csrf", () => ({
  getCsrfToken: () => 'test-csrf-token',
  CSRF_HEADER_NAME: 'X-CSRF-Token',
  isMutatingMethod: (m: string) => ['POST','PUT','PATCH','DELETE'].includes(m.toUpperCase()),
}));

// Mock runtime config
vi.mock("@foundation/src/config/runtime", () => ({
  runtimeConfig: { apiBaseUrl: "http://localhost:5000", baseDomain: "" },
}));

// Mock api-utils
vi.mock("../core/api-utils", async (importOriginal) => {
  const actual = await importOriginal<typeof ApiUtils>();
  return {
    ...actual,
    handleApiError: vi.fn().mockImplementation(async (response: Response) => {
      const text = (await response.text?.()) || `Error ${(response).status}`;
      throw new Error(text);
    }),
    API_BASE_URL: "http://localhost:5000",
  };
});

// Mock fetch
const mockFetch = vi.fn();
global.fetch = mockFetch;

describe("tenants-api", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("canCreateTenant", () => {
    it("returns canCreate status when API returns success", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ canCreate: true, currentCount: 1, maxAllowed: 5 }),
      });

      const result = await canCreateTenant();

      expect(result).toEqual({
        canCreate: true,
        currentCount: 1,
        maxAllowed: 5,
      });
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/tenants/can-create",
        expect.objectContaining({
          method: "GET",
          credentials: "include",
        }),
      );
    });

    it("throws error when API call fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 401,
        text: async () => "Unauthorized",
      });

      await expect(canCreateTenant()).rejects.toThrow("Unauthorized");
    });

    it("includes Content-Type in headers", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ canCreate: true }),
      });

      await canCreateTenant();

      expect(mockFetch).toHaveBeenCalledWith(
        expect.any(String),
        expect.objectContaining({
          headers: expect.objectContaining({
            "Content-Type": "application/json",
          }),
        }),
      );
    });
  });

  describe("createTenant", () => {
    it("creates tenant and returns response", async () => {
      const mockResponse = {
        id: "tenant-123",
        slug: "my-company",
        displayName: "My Company",
        state: "active",
      };
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => mockResponse,
      });

      const result = await createTenant({
        slug: "my-company",
        displayName: "My Company",
      });

      expect(result).toEqual(mockResponse);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/tenants",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({
            slug: "my-company",
            displayName: "My Company",
          }),
        }),
      );
    });

    it("throws error when creation fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 400,
        text: async () => "Slug already exists",
      });

      await expect(
        createTenant({ slug: "test", displayName: "Test" }),
      ).rejects.toThrow("Slug already exists");
    });
  });

  describe("getTenantMemberships", () => {
    it("returns list of memberships", async () => {
      const mockMemberships = [
        {
          tenantId: "tenant-1",
          tenantSlug: "acme",
          tenantDisplayName: "ACME Corp",
          tenantStatus: "active",
          role: "admin",
          status: "active",
          isOwner: true,
          joinedAt: "2024-01-01T00:00:00Z",
        },
      ];
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => mockMemberships,
      });

      const result = await getTenantMemberships();

      expect(result).toEqual(mockMemberships);
      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/tenants/memberships",
        expect.objectContaining({ method: "GET" }),
      );
    });
  });

  describe("leaveTenant", () => {
    it("makes POST request to leave endpoint", async () => {
      mockFetch.mockResolvedValue({ ok: true });

      await leaveTenant("tenant-123");

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/tenants/tenant-123/leave",
        expect.objectContaining({ method: "POST" }),
      );
    });

    it("throws error when leave fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 403,
        text: async () => "Cannot leave as only admin",
      });

      await expect(leaveTenant("tenant-123")).rejects.toThrow(
        "Cannot leave as only admin",
      );
    });
  });

  describe("deleteTenant", () => {
    it("makes DELETE request to tenant endpoint", async () => {
      mockFetch.mockResolvedValue({ ok: true });

      await deleteTenant("tenant-123");

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/tenants/tenant-123",
        expect.objectContaining({ method: "DELETE" }),
      );
    });

    it("throws error when delete fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 403,
        text: async () => "Only owner can delete",
      });

      await expect(deleteTenant("tenant-123")).rejects.toThrow(
        "Only owner can delete",
      );
    });
  });

  describe("getStarterTemplates", () => {
    it("returns list of starter templates", async () => {
      const mockTemplates = [
        {
          key: "blank",
          name: "Blank",
          description: "Start from scratch",
          icon: "blank",
          includesDemoData: false,
        },
        {
          key: "office",
          name: "Office",
          description: "Standard office layout",
          icon: "office",
          includesDemoData: true,
        },
      ];
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => mockTemplates,
      });

      const result = await getStarterTemplates();

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/tenants/starter-templates",
        expect.objectContaining({ method: "GET" }),
      );
      expect(result).toEqual(mockTemplates);
    });

    it("throws error when fetch fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 500,
        text: async () => "Internal Server Error",
      });

      await expect(getStarterTemplates()).rejects.toThrow(
        "Internal Server Error",
      );
    });
  });

  describe("authentication", () => {
    it("uses credentials: include for cookie auth (no Bearer token)", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ canCreate: false }),
      });

      const result = await canCreateTenant();

      expect(result).toEqual({ canCreate: false });
      expect(mockFetch).toHaveBeenCalledWith(
        expect.any(String),
        expect.objectContaining({ credentials: "include" }),
      );
      // No Authorization header in BFF mode
      const [, options] = mockFetch.mock.calls[0] as [string, RequestInit];
      const headers = options.headers as Record<string, string>;
      expect(headers.Authorization).toBeUndefined();
    });
  });

  describe("cancelTenantDeletion", () => {
    it("sends POST to /api/tenants/{id}/cancel-deletion", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        text: async () => "",
      });

      await cancelTenantDeletion("tenant-123");

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/tenants/tenant-123/cancel-deletion",
        expect.objectContaining({
          method: "POST",
          credentials: "include",
        }),
      );
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 400,
        text: async () => "Tenant is not being deleted.",
      });

      await expect(cancelTenantDeletion("tenant-123")).rejects.toThrow(
        "Tenant is not being deleted.",
      );
    });
  });
});
