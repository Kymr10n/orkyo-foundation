/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  useTenantSettings,
  useUpdateTenantSettings,
  useResetTenantSetting,
} from "@foundation/src/hooks/useTenantSettings";
import * as settingsApi from "@foundation/src/lib/api/tenant-settings-api";
import { createTestQueryWrapper } from "@foundation/src/test-utils";

vi.mock("@foundation/src/lib/api/tenant-settings-api");

const mockResponse: settingsApi.TenantSettingsResponse = {
  settings: [
    {
      key: "security.password_min_length",
      category: "security",
      displayName: "Minimum Password Length",
      description: "Minimum number of characters.",
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
      description: "Brand name.",
      valueType: "string",
      defaultValue: "Orkyo",
      scope: "tenant",
      minValue: null,
      maxValue: null,
      currentValue: "Orkyo",
    },
  ],
};

describe("useTenantSettings", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("useTenantSettings query", () => {
    it("fetches tenant settings", async () => {
      vi.mocked(settingsApi.getTenantSettings).mockResolvedValue(mockResponse);

      const { result } = renderHook(() => useTenantSettings(), {
        wrapper: createTestQueryWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockResponse);
      expect(settingsApi.getTenantSettings).toHaveBeenCalled();
    });

    it("exposes loading state", () => {
      vi.mocked(settingsApi.getTenantSettings).mockReturnValue(
        new Promise(() => {}), // never resolves
      );

      const { result } = renderHook(() => useTenantSettings(), {
        wrapper: createTestQueryWrapper(),
      });

      expect(result.current.isLoading).toBe(true);
    });

    it("exposes error state", async () => {
      vi.mocked(settingsApi.getTenantSettings).mockRejectedValue(
        new Error("fail"),
      );

      const { result } = renderHook(() => useTenantSettings(), {
        wrapper: createTestQueryWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
    });

    it("does not fetch when slug is empty string", async () => {
      vi.mocked(settingsApi.getTenantSettings).mockResolvedValue(mockResponse);

      const { result } = renderHook(() => useTenantSettings(""), {
        wrapper: createTestQueryWrapper(),
      });

      await new Promise((r) => setTimeout(r, 50));

      expect(result.current.isLoading).toBe(false);
      expect(result.current.fetchStatus).toBe("idle");
      expect(settingsApi.getTenantSettings).not.toHaveBeenCalled();
    });

    it("passes null to API for site-scope calls", async () => {
      vi.mocked(settingsApi.getTenantSettings).mockResolvedValue(mockResponse);

      const { result } = renderHook(() => useTenantSettings(null), {
        wrapper: createTestQueryWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(settingsApi.getTenantSettings).toHaveBeenCalledWith(null);
    });

    it("passes specific slug to API for cross-tenant calls", async () => {
      vi.mocked(settingsApi.getTenantSettings).mockResolvedValue(mockResponse);

      const { result } = renderHook(() => useTenantSettings("acme"), {
        wrapper: createTestQueryWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(settingsApi.getTenantSettings).toHaveBeenCalledWith("acme");
    });
  });

  describe("useUpdateTenantSettings", () => {
    it("calls updateTenantSettings and invalidates cache", async () => {
      const queryClient = new QueryClient({
        defaultOptions: {
          queries: { retry: false },
          mutations: { retry: false },
        },
      });
      const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");

      const wrapper = ({ children }: { children: React.ReactNode }) => (
        <QueryClientProvider client={queryClient}>
          {children}
        </QueryClientProvider>
      );

      vi.mocked(settingsApi.updateTenantSettings).mockResolvedValue(
        mockResponse,
      );

      const { result } = renderHook(() => useUpdateTenantSettings(), {
        wrapper,
      });

      await result.current.mutateAsync({
        "security.password_min_length": "12",
      });

      expect(settingsApi.updateTenantSettings).toHaveBeenCalledWith({
        "security.password_min_length": "12",
      }, undefined);

      await waitFor(() => {
        expect(invalidateSpy).toHaveBeenCalledWith({
          queryKey: ["tenant-settings"],
        });
      });
    });
  });

  describe("useResetTenantSetting", () => {
    it("calls resetTenantSetting and invalidates cache", async () => {
      const queryClient = new QueryClient({
        defaultOptions: {
          queries: { retry: false },
          mutations: { retry: false },
        },
      });
      const invalidateSpy = vi.spyOn(queryClient, "invalidateQueries");

      const wrapper = ({ children }: { children: React.ReactNode }) => (
        <QueryClientProvider client={queryClient}>
          {children}
        </QueryClientProvider>
      );

      vi.mocked(settingsApi.resetTenantSetting).mockResolvedValue(undefined);

      const { result } = renderHook(() => useResetTenantSetting(), {
        wrapper,
      });

      await result.current.mutateAsync("security.password_min_length");

      expect(settingsApi.resetTenantSetting).toHaveBeenCalledWith(
        "security.password_min_length",
        undefined,
      );

      await waitFor(() => {
        expect(invalidateSpy).toHaveBeenCalledWith({
          queryKey: ["tenant-settings"],
        });
      });
    });
  });
});
