import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { createTestQueryWrapper } from "@foundation/src/test-utils";
import {
  useInsightsOverview,
  useInsightsUtilization,
  useInsightsConflicts,
  useInsightsRequests,
} from "./useInsights";
import * as api from "@foundation/src/lib/api/insights-api";

vi.mock("@foundation/src/lib/api/insights-api", () => ({
  getInsightsOverview: vi.fn(() => Promise.resolve({ ok: "overview" })),
  getInsightsUtilization: vi.fn(() => Promise.resolve({ ok: "utilization" })),
  getInsightsConflicts: vi.fn(() => Promise.resolve({ ok: "conflicts" })),
  getInsightsRequests: vi.fn(() => Promise.resolve({ ok: "requests" })),
}));

const from = new Date("2026-01-01T00:00:00Z");
const to = new Date("2026-12-31T00:00:00Z");

describe("useInsights hooks", () => {
  beforeEach(() => vi.clearAllMocks());

  it("useInsightsOverview calls the API with (from, to, siteId)", async () => {
    const { result } = renderHook(() => useInsightsOverview("site-1", from, to), {
      wrapper: createTestQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(api.getInsightsOverview).toHaveBeenCalledWith(from, to, "site-1");
  });

  it("useInsightsUtilization passes resourceType + bucket through", async () => {
    const { result } = renderHook(
      () => useInsightsUtilization("space", "site-1", from, to, "month"),
      { wrapper: createTestQueryWrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(api.getInsightsUtilization).toHaveBeenCalledWith("space", from, to, "month", "site-1");
  });

  it("useInsightsConflicts calls the API with the bucket", async () => {
    const { result } = renderHook(() => useInsightsConflicts(null, from, to, "quarter"), {
      wrapper: createTestQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(api.getInsightsConflicts).toHaveBeenCalledWith(from, to, "quarter", null);
  });

  it("useInsightsRequests calls the API with the bucket", async () => {
    const { result } = renderHook(() => useInsightsRequests("site-2", from, to, "week"), {
      wrapper: createTestQueryWrapper(),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(api.getInsightsRequests).toHaveBeenCalledWith(from, to, "week", "site-2");
  });
});
