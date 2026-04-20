/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { usePreferences, useUpdatePreferences, type UserPreferences } from "./usePreferences";
import { createTestQueryWrapper, createTestQueryClientWithSpy } from "@/test-utils";

vi.mock("@/lib/core/api-client", () => ({
  apiGet: vi.fn(),
  apiPut: vi.fn(),
}));

import * as apiClient from "@/lib/core/api-client";

const mockPreferences: UserPreferences = {
  spaceOrder: ["space-1", "space-2", "space-3"],
  theme: "dark",
  language: "en",
};

describe("usePreferences", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe("usePreferences query", () => {
    it("fetches preferences on mount with correct path", async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue(mockPreferences);

      const { result } = renderHook(() => usePreferences(), {
        wrapper: createTestQueryWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(apiClient.apiGet).toHaveBeenCalledWith("/api/preferences");
    });

    it("returns data when successful", async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue(mockPreferences);

      const { result } = renderHook(() => usePreferences(), {
        wrapper: createTestQueryWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));

      expect(result.current.data).toEqual(mockPreferences);
    });

    it("is in loading state initially", () => {
      vi.mocked(apiClient.apiGet).mockReturnValue(new Promise(() => {})); // never resolves

      const { result } = renderHook(() => usePreferences(), {
        wrapper: createTestQueryWrapper(),
      });

      expect(result.current.isLoading).toBe(true);
      expect(result.current.data).toBeUndefined();
    });

    it("exposes error state when fetch fails", async () => {
      vi.mocked(apiClient.apiGet).mockRejectedValue(new Error("Network error"));

      const { result } = renderHook(() => usePreferences(), {
        wrapper: createTestQueryWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true), { timeout: 3000 });

      expect(result.current.error).toBeInstanceOf(Error);
      expect((result.current.error!).message).toBe("Network error");
    });

    it("handles empty preferences object", async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue({});

      const { result } = renderHook(() => usePreferences(), {
        wrapper: createTestQueryWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual({});
    });
  });

  describe("useUpdatePreferences mutation", () => {
    it("calls apiPut with the correct path and payload", async () => {
      vi.mocked(apiClient.apiPut).mockResolvedValue(undefined);

      const { queryClient, wrapper } = createTestQueryClientWithSpy();

      const { result } = renderHook(() => useUpdatePreferences(), { wrapper });

      const updates: UserPreferences = {
        spaceOrder: ["space-2", "space-1"],
        theme: "light",
      };

      await result.current.mutateAsync(updates);

      expect(apiClient.apiPut).toHaveBeenCalledWith("/api/preferences", updates);

      // suppress unused variable warning
      void queryClient;
    });

    it("invalidates the preferences query key on success", async () => {
      vi.mocked(apiClient.apiPut).mockResolvedValue(undefined);

      const { spy, wrapper } = createTestQueryClientWithSpy();

      const { result } = renderHook(() => useUpdatePreferences(), { wrapper });

      await result.current.mutateAsync({ spaceOrder: ["space-1"] });

      await waitFor(() => {
        expect(spy).toHaveBeenCalledWith({ queryKey: ["preferences"] });
      });
    });

    it("does not invalidate if apiPut rejects", async () => {
      vi.mocked(apiClient.apiPut).mockRejectedValue(new Error("Save failed"));

      const { spy, wrapper } = createTestQueryClientWithSpy();

      const { result } = renderHook(() => useUpdatePreferences(), { wrapper });

      await expect(
        result.current.mutateAsync({ spaceOrder: [] })
      ).rejects.toThrow("Save failed");

      expect(spy).not.toHaveBeenCalled();
    });

    it("exposes isSuccess after a successful mutation", async () => {
      vi.mocked(apiClient.apiPut).mockResolvedValue(undefined);

      const { result } = renderHook(() => useUpdatePreferences(), {
        wrapper: createTestQueryWrapper(),
      });

      await result.current.mutateAsync({ theme: "dark" });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
    });

    it("exposes isError after a failed mutation", async () => {
      vi.mocked(apiClient.apiPut).mockRejectedValue(new Error("Unauthorized"));

      const { result } = renderHook(() => useUpdatePreferences(), {
        wrapper: createTestQueryWrapper(),
      });

      await expect(
        result.current.mutateAsync({ theme: "dark" })
      ).rejects.toThrow();

      await waitFor(() => expect(result.current.isError).toBe(true));
    });
  });
});
