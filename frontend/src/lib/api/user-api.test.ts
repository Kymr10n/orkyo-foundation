import { beforeEach, describe, expect, it, vi } from "vitest";
import type * as ApiUtils from "../core/api-utils";
import {
  getUsers,
  getInvitations,
  createInvitation,
  resendInvitation,
  cancelInvitation,
  updateUserRole,
  deleteUser,
} from "./user-api";

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

const USER_ID = "user-123";
const INVITATION_ID = "inv-789";

const mockUser = {
  id: USER_ID,
  email: "test@example.com",
  displayName: "Test User",
  role: "editor" as const,
  status: "active" as const,
  createdAt: "2025-01-01T00:00:00Z",
  lastLoginAt: "2025-03-01T10:00:00Z",
};

const mockInvitation = {
  id: INVITATION_ID,
  email: "invite@example.com",
  role: "viewer" as const,
  invitedBy: "admin@example.com",
  tokenHash: "abc123hash",
  expiresAt: "2026-05-01T00:00:00Z",
  createdAt: "2026-04-01T00:00:00Z",
};

// ============================================================================
// getUsers
// ============================================================================

describe("user-api", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("getUsers", () => {
    it("sends GET to /api/users", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ users: [mockUser] }),
      });

      await getUsers();

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/users",
        expect.objectContaining({ method: "GET", credentials: "include" }),
      );
    });

    it("returns the users array extracted from the response", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ users: [mockUser] }),
      });

      const result = await getUsers();

      expect(result).toEqual([mockUser]);
    });

    it("returns an empty array when users is missing from response", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({}),
      });

      const result = await getUsers();

      expect(result).toEqual([]);
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 403,
        text: async () => "Forbidden",
      });

      await expect(getUsers()).rejects.toThrow("Forbidden");
    });
  });

  // ============================================================================
  // getInvitations
  // ============================================================================

  describe("getInvitations", () => {
    it("sends GET to /api/users/invitations", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ invitations: [mockInvitation] }),
      });

      await getInvitations();

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/users/invitations",
        expect.objectContaining({ method: "GET", credentials: "include" }),
      );
    });

    it("returns the invitations array extracted from the response", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ invitations: [mockInvitation] }),
      });

      const result = await getInvitations();

      expect(result).toEqual([mockInvitation]);
    });

    it("returns an empty array when invitations is missing from response", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({}),
      });

      const result = await getInvitations();

      expect(result).toEqual([]);
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 403,
        text: async () => "Forbidden",
      });

      await expect(getInvitations()).rejects.toThrow("Forbidden");
    });
  });

  // ============================================================================
  // createInvitation
  // ============================================================================

  describe("createInvitation", () => {
    const payload = { email: "invite@example.com", role: "viewer" as const };

    it("sends POST to /api/users/invite with body", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => mockInvitation,
      });

      await createInvitation(payload);

      expect(mockFetch).toHaveBeenCalledWith(
        "http://localhost:5000/api/users/invite",
        expect.objectContaining({
          method: "POST",
          credentials: "include",
          body: JSON.stringify(payload),
        }),
      );
    });

    it("returns the created invitation", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => mockInvitation,
      });

      const result = await createInvitation(payload);

      expect(result).toEqual(mockInvitation);
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 409,
        text: async () => "User already invited",
      });

      await expect(createInvitation(payload)).rejects.toThrow(
        "User already invited",
      );
    });
  });

  // ============================================================================
  // resendInvitation
  // ============================================================================

  describe("resendInvitation", () => {
    it("sends POST to /api/users/invitations/{invitationId}/resend", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      await resendInvitation(INVITATION_ID);

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5000/api/users/invitations/${INVITATION_ID}/resend`,
        expect.objectContaining({
          method: "POST",
          credentials: "include",
        }),
      );
    });

    it("resolves to void on success", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      const result = await resendInvitation(INVITATION_ID);

      expect(result).toBeUndefined();
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 404,
        text: async () => "Invitation not found",
      });

      await expect(resendInvitation(INVITATION_ID)).rejects.toThrow(
        "Invitation not found",
      );
    });
  });

  // ============================================================================
  // cancelInvitation
  // ============================================================================

  describe("cancelInvitation", () => {
    it("sends DELETE to /api/users/invitations/{invitationId}", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      await cancelInvitation(INVITATION_ID);

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5000/api/users/invitations/${INVITATION_ID}`,
        expect.objectContaining({
          method: "DELETE",
          credentials: "include",
        }),
      );
    });

    it("resolves to void on success", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      const result = await cancelInvitation(INVITATION_ID);

      expect(result).toBeUndefined();
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 404,
        text: async () => "Invitation not found",
      });

      await expect(cancelInvitation(INVITATION_ID)).rejects.toThrow(
        "Invitation not found",
      );
    });
  });

  // ============================================================================
  // updateUserRole
  // ============================================================================

  describe("updateUserRole", () => {
    const payload = { role: "admin" as const };

    it("sends PATCH to /api/users/{userId}/role with body", async () => {
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => ({ ...mockUser, role: "admin" }),
      });

      await updateUserRole(USER_ID, payload);

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5000/api/users/${USER_ID}/role`,
        expect.objectContaining({
          method: "PATCH",
          credentials: "include",
          body: JSON.stringify(payload),
        }),
      );
    });

    it("returns the updated user", async () => {
      const updatedUser = { ...mockUser, role: "admin" as const };
      mockFetch.mockResolvedValue({
        ok: true,
        json: async () => updatedUser,
      });

      const result = await updateUserRole(USER_ID, payload);

      expect(result).toEqual(updatedUser);
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 404,
        text: async () => "User not found",
      });

      await expect(updateUserRole(USER_ID, payload)).rejects.toThrow(
        "User not found",
      );
    });
  });

  // ============================================================================
  // deleteUser
  // ============================================================================

  describe("deleteUser", () => {
    it("sends DELETE to /api/users/{userId}", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      await deleteUser(USER_ID);

      expect(mockFetch).toHaveBeenCalledWith(
        `http://localhost:5000/api/users/${USER_ID}`,
        expect.objectContaining({
          method: "DELETE",
          credentials: "include",
        }),
      );
    });

    it("resolves to void on success", async () => {
      mockFetch.mockResolvedValue({ ok: true, json: async () => null });

      const result = await deleteUser(USER_ID);

      expect(result).toBeUndefined();
    });

    it("throws when the request fails", async () => {
      mockFetch.mockResolvedValue({
        ok: false,
        status: 404,
        text: async () => "User not found",
      });

      await expect(deleteUser(USER_ID)).rejects.toThrow("User not found");
    });
  });
});
