import { describe, it, expect, vi, beforeEach } from "vitest";
import { registerInterest } from "./interest-api";
import * as apiClient from "../core/api-client";
import { API_PATHS } from "../core/api-paths";
import { TENANT_HEADER_NAME } from "@foundation/src/constants/http";

vi.mock("../core/api-client");

describe("interest-api", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("registerInterest", () => {
    it("calls apiPost with correct path and request body", async () => {
      const mockResponse = {
        message: "Interest registered",
        registrationId: "reg-123",
      };
      vi.mocked(apiClient.apiPost).mockResolvedValue(mockResponse);

      const result = await registerInterest({
        email: "user@example.com",
        tier: 1,
      });

      expect(apiClient.apiPost).toHaveBeenCalledWith(
        API_PATHS.INTEREST,
        {
          email: "user@example.com",
          tier: 1,
          organizationId: null,
          source: "onboarding",
        },
        { omitHeaders: [TENANT_HEADER_NAME] },
      );
      expect(result).toEqual(mockResponse);
    });

    it("passes organizationId and source when provided", async () => {
      vi.mocked(apiClient.apiPost).mockResolvedValue({ message: "OK" });

      await registerInterest({
        email: "user@example.com",
        tier: 2,
        organizationId: "org-456",
        source: "pricing-page",
      });

      expect(apiClient.apiPost).toHaveBeenCalledWith(
        API_PATHS.INTEREST,
        {
          email: "user@example.com",
          tier: 2,
          organizationId: "org-456",
          source: "pricing-page",
        },
        { omitHeaders: [TENANT_HEADER_NAME] },
      );
    });

    it("omits tenant header (endpoint is anonymous)", async () => {
      vi.mocked(apiClient.apiPost).mockResolvedValue({ message: "OK" });

      await registerInterest({ email: "a@b.com", tier: 1 });

      const options = vi.mocked(apiClient.apiPost).mock.calls[0][2];
      expect(options?.omitHeaders).toContain(TENANT_HEADER_NAME);
    });

    it("propagates API errors", async () => {
      vi.mocked(apiClient.apiPost).mockRejectedValue(
        new Error("Invalid email"),
      );

      await expect(
        registerInterest({ email: "bad", tier: 1 }),
      ).rejects.toThrow("Invalid email");
    });
  });
});
