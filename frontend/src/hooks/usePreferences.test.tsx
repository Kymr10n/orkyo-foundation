/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { usePreferences, useUpdatePreferences, type UserPreferences } from "./usePreferences";
import { createTestQueryWrapper } from "@foundation/src/test-utils";
import { createFeedbackMutationCache } from "@foundation/src/lib/core/query-client";

vi.mock("@foundation/src/lib/core/api-client", () => ({
  apiGet: vi.fn(),
  apiPut: vi.fn(),
}));
vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

// The update mutation invalidates through the meta-driven MutationCache.
function createFeedbackClientWithSpy() {
  const client: QueryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    mutationCache: createFeedbackMutationCache(() => client),
  });
  const spy = vi.spyOn(client, "invalidateQueries");
  const wrapper = ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
  return { client, spy, wrapper };
}

import * as apiClient from "@foundation/src/lib/core/api-client";

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

      const { wrapper } = createFeedbackClientWithSpy();

      const { result } = renderHook(() => useUpdatePreferences(), { wrapper });

      const updates: UserPreferences = {
        spaceOrder: ["space-2", "space-1"],
        theme: "light",
      };

      await result.current.mutateAsync(updates);

      expect(apiClient.apiPut).toHaveBeenCalledWith("/api/preferences", updates);
    });

    it("invalidates the preferences query key on success", async () => {
      vi.mocked(apiClient.apiPut).mockResolvedValue(undefined);

      const { spy, wrapper } = createFeedbackClientWithSpy();

      const { result } = renderHook(() => useUpdatePreferences(), { wrapper });

      await result.current.mutateAsync({ spaceOrder: ["space-1"] });

      await waitFor(() => {
        expect(spy).toHaveBeenCalledWith({ queryKey: ["preferences"], exact: false });
      });
    });

    it("does not invalidate if apiPut rejects", async () => {
      vi.mocked(apiClient.apiPut).mockRejectedValue(new Error("Save failed"));

      const { spy, wrapper } = createFeedbackClientWithSpy();

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
