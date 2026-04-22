import { describe, it, expect, vi, beforeEach } from "vitest";
import {
  getTenantSettings,
  updateTenantSettings,
  resetTenantSetting,
} from "./tenant-settings-api";
import * as apiClient from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

vi.mock("../core/api-client");

const mockSettingsResponse = {
  settings: [
    {
      key: "security.password_min_length",
      category: "security",
      displayName: "Minimum Password Length",
      description: "Minimum number of characters required for passwords.",
      valueType: "int",
      defaultValue: "8",
      scope: "tenant",
      minValue: "6",
      maxValue: "128",
      currentValue: "8",
    },
    {
      key: "branding.branding_product_name",
      category: "branding",
      displayName: "Product Name",
      description: "Brand name shown in emails and UI.",
      valueType: "string",
      defaultValue: "Orkyo",
      scope: "tenant",
      minValue: null,
      maxValue: null,
      currentValue: "Acme Corp",
    },
  ],
};

describe("tenant-settings-api", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("getTenantSettings", () => {
    it("calls apiGet with correct endpoint", async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue(mockSettingsResponse);

      const result = await getTenantSettings();

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.SETTINGS, undefined);
      expect(result).toEqual(mockSettingsResponse);
    });

    it("returns settings array from response", async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue(mockSettingsResponse);

      const result = await getTenantSettings();

      expect(result.settings).toHaveLength(2);
      expect(result.settings[0].key).toBe("security.password_min_length");
    });

    it("passes null slug as omitHeaders for site context", async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue(mockSettingsResponse);

      await getTenantSettings(null);

      expect(apiClient.apiGet).toHaveBeenCalledWith(
        API_PATHS.SETTINGS,
        expect.objectContaining({ omitHeaders: ["X-Tenant-Slug"] }),
      );
    });

    it("passes string slug as tenant header override", async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue(mockSettingsResponse);

      await getTenantSettings("acme");

      expect(apiClient.apiGet).toHaveBeenCalledWith(
        API_PATHS.SETTINGS,
        expect.objectContaining({ headers: { "X-Tenant-Slug": "acme" } }),
      );
    });
  });

  describe("updateTenantSettings", () => {
    it("calls apiPut with correct endpoint and wrapped payload", async () => {
      const updates = { "security.password_min_length": "12" };
      vi.mocked(apiClient.apiPut).mockResolvedValue(mockSettingsResponse);

      const result = await updateTenantSettings(updates);

      expect(apiClient.apiPut).toHaveBeenCalledWith(API_PATHS.SETTINGS, {
        settings: updates,
      }, undefined);
      expect(result).toEqual(mockSettingsResponse);
    });

    it("sends multiple settings in one call", async () => {
      const updates = {
        "security.password_min_length": "10",
        "branding.branding_product_name": "New Name",
      };
      vi.mocked(apiClient.apiPut).mockResolvedValue(mockSettingsResponse);

      await updateTenantSettings(updates);

      expect(apiClient.apiPut).toHaveBeenCalledWith(API_PATHS.SETTINGS, {
        settings: updates,
      }, undefined);
    });
  });

  describe("resetTenantSetting", () => {
    it("calls apiDelete with correct endpoint including key", async () => {
      vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

      await resetTenantSetting("security.password_min_length");

      expect(apiClient.apiDelete).toHaveBeenCalledWith(
        API_PATHS.setting("security.password_min_length"),
        undefined,
      );
    });

    it("builds correct URL for dotted keys", async () => {
      vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

      await resetTenantSetting("branding.branding_product_name");

      expect(apiClient.apiDelete).toHaveBeenCalledWith(
        "/api/settings/branding.branding_product_name",
        undefined,
      );
    });
  });
});
