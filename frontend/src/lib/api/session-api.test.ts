import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as ApiUtils from "../core/api-utils";
import { acceptTos, markTourSeen } from "./session-api";

// Mock AuthContext (BFF mode — getAuthTokenSync returns null)
vi.mock("@/contexts/AuthContext", () => ({
  getAuthTokenSync: () => null,
  getTenantSlugSync: () => null,
}));

// Mock CSRF
vi.mock("@/lib/core/csrf", () => ({
  getCsrfToken: () => 'test-csrf-token',
  CSRF_HEADER_NAME: 'X-CSRF-Token',
  isMutatingMethod: (m: string) => ['POST','PUT','PATCH','DELETE'].includes(m.toUpperCase()),
}));

// Mock runtime config
vi.mock("@/config/runtime", () => ({
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

describe("session-api", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("acceptTos", () => {
    it("sends POST request to accept ToS", async () => {
      mockFetch.mockResolvedValue({ ok: true });

      await acceptTos("2026-02");

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/session/tos/accept",
        expect.objectContaining({
          method: "POST",
          credentials: "include",
          body: JSON.stringify({ tosVersion: "2026-02" }),
          headers: expect.objectContaining({
            "Content-Type": "application/json",
            "X-CSRF-Token": "test-csrf-token",
          }),
        }),
      );
    });

    it("does not include Authorization header (BFF mode)", async () => {
      mockFetch.mockResolvedValue({ ok: true });

      await acceptTos("2026-02");

      const [, options] = mockFetch.mock.calls[0] as [string, RequestInit];
      const headers = options.headers as Record<string, string>;
      expect(headers.Authorization).toBeUndefined();
    });

    it("throws error when API call fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 401,
        text: async () => "Not authenticated",
      });

      await expect(acceptTos("2026-02")).rejects.toThrow("Not authenticated");
    });

    it("works without CSRF token", async () => {
      mockFetch.mockResolvedValue({ ok: true });

      await acceptTos("2026-02");

      expect(mockFetch).toHaveBeenCalled();
    });
  });

  // ========================================================================
  // markTourSeen
  // ========================================================================

  describe("markTourSeen", () => {
    it("sends POST to /api/session/tour/seen", async () => {
      mockFetch.mockResolvedValue({ ok: true });

      await markTourSeen();

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/session/tour/seen",
        expect.objectContaining({
          method: "POST",
          credentials: "include",
          headers: expect.objectContaining({
            "Content-Type": "application/json",
          }),
        }),
      );
    });

    it("resolves to void on success", async () => {
      mockFetch.mockResolvedValue({ ok: true });

      const result = await markTourSeen();

      expect(result).toBeUndefined();
    });

    it("throws error when API call fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 401,
        text: async () => "Unauthorized",
      });

      await expect(markTourSeen()).rejects.toThrow("Unauthorized");
    });

    it("does not include Authorization header (BFF uses cookies)", async () => {
      mockFetch.mockResolvedValue({ ok: true });

      await markTourSeen();

      const [, options] = mockFetch.mock.calls[0] as [string, RequestInit];
      const headers = options.headers as Record<string, string>;
      expect(headers.Authorization).toBeUndefined();
    });
  });
});
