import { describe, it, expect, vi, beforeEach } from "vitest";
import {
  updateTenant,
  transferTenantOwnership,
} from "./tenant-management-api";
import * as apiClient from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

vi.mock("../core/api-client");

const mockTenant = {
  id: "tenant-123",
  slug: "acme",
  displayName: "ACME Corp",
  status: "active" as const,
};

describe("tenant-api", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("updateTenant", () => {
    it("calls apiPatch with tenant ID and data", async () => {
      vi.mocked(apiClient.apiPatch).mockResolvedValue({
        ...mockTenant,
        displayName: "New Name",
      });

      const result = await updateTenant("tenant-123", {
        displayName: "New Name",
      });

      expect(apiClient.apiPatch).toHaveBeenCalledWith(
        API_PATHS.TENANTS.byId("tenant-123"),
        { displayName: "New Name" },
      );
      expect(result.displayName).toBe("New Name");
    });

    it("propagates API errors", async () => {
      vi.mocked(apiClient.apiPatch).mockRejectedValue(
        new Error("Forbidden"),
      );

      await expect(
        updateTenant("tenant-123", { displayName: "X" }),
      ).rejects.toThrow("Forbidden");
    });
  });

  describe("transferTenantOwnership", () => {
    it("calls apiPost with correct path and new owner ID", async () => {
      vi.mocked(apiClient.apiPost).mockResolvedValue({ transferred: true });

      const result = await transferTenantOwnership("tenant-123", "user-456");

      expect(apiClient.apiPost).toHaveBeenCalledWith(
        API_PATHS.TENANTS.transferOwnership("tenant-123"),
        { newOwnerId: "user-456" },
      );
      expect(result).toEqual({ transferred: true });
    });

    it("propagates API errors", async () => {
      vi.mocked(apiClient.apiPost).mockRejectedValue(
        new Error("User is not an admin"),
      );

      await expect(
        transferTenantOwnership("tenant-123", "user-999"),
      ).rejects.toThrow("User is not an admin");
    });
  });
  // deleteTenant and leaveTenant tests are in tenants-api.test.ts (DRY consolidation)
});
