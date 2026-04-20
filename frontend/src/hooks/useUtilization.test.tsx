/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, act, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useRequests, useSpaces, useScheduleRequest } from "./useUtilization";
import { createTestQueryWrapper } from "@/test-utils";
import type { Request } from "@/types/requests";
import type { Space } from "@/types/space";

vi.mock("@/lib/api/utilization-api");

import * as utilizationApi from "@/lib/api/utilization-api";

// ---------------------------------------------------------------------------
// Shared mock data
// ---------------------------------------------------------------------------

const mockRequest: Request = {
  id: "req-001",
  name: "Deep-Sea Survey",
  description: "Seismic scan of sector 7",
  spaceId: "space-A",
  startTs: "2026-04-01T08:00:00Z",
  endTs: "2026-04-01T10:00:00Z",
  earliestStartTs: null,
  latestEndTs: null,
  minimalDurationValue: 90,
  minimalDurationUnit: "minutes",
  actualDurationValue: 120,
  actualDurationUnit: "minutes",
  durationMin: 90,
  status: "planned",
  requirements: [],
  schedulingSettingsApply: true,
  planningMode: "leaf",
  sortOrder: 0,
  createdAt: "2026-03-20T09:00:00Z",
  updatedAt: "2026-03-25T14:00:00Z",
};

const mockRequest2: Request = {
  id: "req-002",
  name: "Core Sample Analysis",
  description: null,
  spaceId: null,
  startTs: null,
  endTs: null,
  earliestStartTs: "2026-04-02T00:00:00Z",
  latestEndTs: "2026-04-05T23:59:59Z",
  minimalDurationValue: 2,
  minimalDurationUnit: "hours",
  actualDurationValue: null,
  actualDurationUnit: null,
  durationMin: 120,
  status: "planned",
  requirements: [],
  schedulingSettingsApply: true,
  planningMode: "leaf",
  sortOrder: 0,
  createdAt: "2026-03-21T11:00:00Z",
  updatedAt: "2026-03-21T11:00:00Z",
};

const mockSpace: Space = {
  id: "space-A",
  siteId: "site-001",
  name: "Lab Alpha",
  code: "LAB-A",
  description: "Primary lab space",
  isPhysical: true,
  geometry: {
    type: "rectangle",
    coordinates: [
      { x: 0, y: 0 },
      { x: 10, y: 0 },
      { x: 10, y: 5 },
      { x: 0, y: 5 },
    ],
  },
  properties: { capacity: 12 },
  groupId: "group-001",
  capacity: 12,
  createdAt: "2026-01-10T08:00:00Z",
  updatedAt: "2026-01-10T08:00:00Z",
};

const mockSpace2: Space = {
  id: "space-B",
  siteId: "site-001",
  name: "Lab Beta",
  code: "LAB-B",
  description: "Secondary lab space",
  isPhysical: true,
  geometry: undefined,
  properties: {},
  groupId: undefined,
  capacity: 1,
  createdAt: "2026-01-11T08:00:00Z",
  updatedAt: "2026-01-11T08:00:00Z",
};

// ---------------------------------------------------------------------------
// Helper: build a QueryClient + wrapper with direct access to the client
// ---------------------------------------------------------------------------

function createClientAndWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });
  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
  return { queryClient, wrapper };
}

// ---------------------------------------------------------------------------
// useRequests
// ---------------------------------------------------------------------------

describe("useRequests", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("fetches requests via fetchRequests", async () => {
    vi.mocked(utilizationApi.fetchRequests).mockResolvedValue([
      mockRequest,
      mockRequest2,
    ]);

    const { result } = renderHook(() => useRequests(), {
      wrapper: createTestQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(utilizationApi.fetchRequests).toHaveBeenCalledTimes(1);
  });

  it("returns data on success", async () => {
    vi.mocked(utilizationApi.fetchRequests).mockResolvedValue([
      mockRequest,
      mockRequest2,
    ]);

    const { result } = renderHook(() => useRequests(), {
      wrapper: createTestQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(result.current.data).toEqual([mockRequest, mockRequest2]);
  });

  it("is in loading state before the promise resolves", () => {
    vi.mocked(utilizationApi.fetchRequests).mockReturnValue(
      new Promise(() => {}) // never resolves
    );

    const { result } = renderHook(() => useRequests(), {
      wrapper: createTestQueryWrapper(),
    });

    expect(result.current.isLoading).toBe(true);
    expect(result.current.data).toBeUndefined();
  });

  it("exposes error state when fetch fails", async () => {
    vi.mocked(utilizationApi.fetchRequests).mockRejectedValue(
      new Error("API error")
    );

    const { result } = renderHook(() => useRequests(), {
      wrapper: createTestQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect((result.current.error!).message).toBe("API error");
  });
});

// ---------------------------------------------------------------------------
// useSpaces
// ---------------------------------------------------------------------------

describe("useSpaces", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("fetches spaces for the given siteId", async () => {
    vi.mocked(utilizationApi.fetchSpaces).mockResolvedValue([
      mockSpace,
      mockSpace2,
    ]);

    const { result } = renderHook(() => useSpaces("site-001"), {
      wrapper: createTestQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(utilizationApi.fetchSpaces).toHaveBeenCalledWith("site-001");
    expect(result.current.data).toEqual([mockSpace, mockSpace2]);
  });

  it("is disabled (not called) when siteId is null", async () => {
    vi.mocked(utilizationApi.fetchSpaces).mockResolvedValue([mockSpace]);

    const { result } = renderHook(() => useSpaces(null), {
      wrapper: createTestQueryWrapper(),
    });

    // Wait a tick to let any pending queries fire
    await new Promise((r) => setTimeout(r, 50));

    expect(result.current.isLoading).toBe(false);
    expect(result.current.fetchStatus).toBe("idle");
    expect(utilizationApi.fetchSpaces).not.toHaveBeenCalled();
  });

  it("refetches when siteId changes from null to a value", async () => {
    vi.mocked(utilizationApi.fetchSpaces).mockResolvedValue([mockSpace]);

    let siteId: string | null = null;

    const { result, rerender } = renderHook(() => useSpaces(siteId), {
      wrapper: createTestQueryWrapper(),
    });

    // Initially disabled
    await new Promise((r) => setTimeout(r, 30));
    expect(utilizationApi.fetchSpaces).not.toHaveBeenCalled();

    // Enable by providing a siteId
    siteId = "site-001";
    rerender();

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(utilizationApi.fetchSpaces).toHaveBeenCalledWith("site-001");
  });
});

// ---------------------------------------------------------------------------
// useScheduleRequest
// ---------------------------------------------------------------------------

describe("useScheduleRequest", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("calls scheduleRequest with the correct requestId and data", async () => {
    const updatedRequest: Request = {
      ...mockRequest,
      spaceId: "space-B",
      startTs: "2026-04-02T09:00:00Z",
      endTs: "2026-04-02T11:00:00Z",
    };

    vi.mocked(utilizationApi.scheduleRequest).mockResolvedValue(updatedRequest);

    const { queryClient, wrapper } = createClientAndWrapper();
    queryClient.setQueryData<Request[]>(["requests"], [mockRequest]);

    const { result } = renderHook(() => useScheduleRequest(), { wrapper });

    const scheduleData: utilizationApi.ScheduleRequestData = {
      spaceId: "space-B",
      startTs: "2026-04-02T09:00:00Z",
      endTs: "2026-04-02T11:00:00Z",
    };

    await act(async () => {
      await result.current.mutateAsync({
        requestId: "req-001",
        data: scheduleData,
      });
    });

    expect(utilizationApi.scheduleRequest).toHaveBeenCalledWith(
      "req-001",
      scheduleData
    );
  });

  it("optimistically updates the cache before mutation settles", async () => {
    // Use a deferred promise so we can inspect the cache while the mutation is
    // still in-flight.
    let resolve!: (value: Request) => void;
    const deferred = new Promise<Request>((res) => {
      resolve = res;
    });

    vi.mocked(utilizationApi.scheduleRequest).mockReturnValue(deferred);

    const { queryClient, wrapper } = createClientAndWrapper();

    // Seed the cache with the original request
    queryClient.setQueryData<Request[]>(["requests"], [mockRequest]);

    const { result } = renderHook(() => useScheduleRequest(), { wrapper });

    const scheduleData: utilizationApi.ScheduleRequestData = {
      spaceId: "space-B",
      startTs: "2026-04-02T09:00:00Z",
      endTs: "2026-04-02T11:00:00Z",
    };

    // Fire the mutation but do not await — we want to check the cache while it
    // is still pending.
    act(() => {
      result.current.mutate({ requestId: "req-001", data: scheduleData });
    });

    // Wait for the optimistic update to be applied
    await waitFor(() => {
      const cached = queryClient.getQueryData<Request[]>(["requests"]);
      const optimistic = cached?.find((r) => r.id === "req-001");
      expect(optimistic?.spaceId).toBe("space-B");
      expect(optimistic?.startTs).toBe("2026-04-02T09:00:00Z");
      expect(optimistic?.endTs).toBe("2026-04-02T11:00:00Z");
    });

    // Fields not included in the mutation should be preserved
    const cached = queryClient.getQueryData<Request[]>(["requests"]);
    const optimistic = cached?.find((r) => r.id === "req-001");
    expect(optimistic?.name).toBe("Deep-Sea Survey");
    expect(optimistic?.status).toBe("planned");

    // Let the mutation complete
    resolve({
      ...mockRequest,
      spaceId: "space-B",
      startTs: "2026-04-02T09:00:00Z",
      endTs: "2026-04-02T11:00:00Z",
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it("rolls back to original cache on error", async () => {
    vi.mocked(utilizationApi.scheduleRequest).mockRejectedValue(
      new Error("Conflict detected")
    );

    const { queryClient, wrapper } = createClientAndWrapper();

    // Seed the cache
    queryClient.setQueryData<Request[]>(["requests"], [mockRequest]);

    const { result } = renderHook(() => useScheduleRequest(), { wrapper });

    await act(async () => {
      try {
        await result.current.mutateAsync({
          requestId: "req-001",
          data: { spaceId: "space-B" },
        });
      } catch {
        // expected
      }
    });

    await waitFor(() => expect(result.current.isError).toBe(true));

    // Cache should be rolled back to the original snapshot
    const cached = queryClient.getQueryData<Request[]>(["requests"]);
    const restored = cached?.find((r) => r.id === "req-001");
    expect(restored?.spaceId).toBe("space-A"); // original value
    expect(restored?.startTs).toBe("2026-04-01T08:00:00Z"); // original value
  });

  it("merges server response into cache on success", async () => {
    // The server may return extra computed fields (e.g. actualDurationValue)
    const serverResponse: Request = {
      ...mockRequest,
      spaceId: "space-B",
      startTs: "2026-04-02T09:00:00Z",
      endTs: "2026-04-02T11:30:00Z",
      actualDurationValue: 150,
      actualDurationUnit: "minutes",
    };

    vi.mocked(utilizationApi.scheduleRequest).mockResolvedValue(serverResponse);

    const { queryClient, wrapper } = createClientAndWrapper();
    queryClient.setQueryData<Request[]>(["requests"], [mockRequest, mockRequest2]);

    const { result } = renderHook(() => useScheduleRequest(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({
        requestId: "req-001",
        data: {
          spaceId: "space-B",
          startTs: "2026-04-02T09:00:00Z",
          endTs: "2026-04-02T11:30:00Z",
        },
      });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const cached = queryClient.getQueryData<Request[]>(["requests"]);

    // Updated entry should have server-confirmed values
    const updated = cached?.find((r) => r.id === "req-001");
    expect(updated?.spaceId).toBe("space-B");
    expect(updated?.endTs).toBe("2026-04-02T11:30:00Z");
    expect(updated?.actualDurationValue).toBe(150);
    expect(updated?.actualDurationUnit).toBe("minutes");

    // Unrelated entry should be unchanged
    const untouched = cached?.find((r) => r.id === "req-002");
    expect(untouched?.spaceId).toBe(null);
  });

  it("unscheduled request: schedules into a space (spaceId was null)", async () => {
    const serverResponse: Request = {
      ...mockRequest2,
      spaceId: "space-A",
      startTs: "2026-04-03T10:00:00Z",
      endTs: "2026-04-03T12:00:00Z",
    };

    vi.mocked(utilizationApi.scheduleRequest).mockResolvedValue(serverResponse);

    const { queryClient, wrapper } = createClientAndWrapper();
    queryClient.setQueryData<Request[]>(["requests"], [mockRequest2]);

    const { result } = renderHook(() => useScheduleRequest(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({
        requestId: "req-002",
        data: {
          spaceId: "space-A",
          startTs: "2026-04-03T10:00:00Z",
          endTs: "2026-04-03T12:00:00Z",
        },
      });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const cached = queryClient.getQueryData<Request[]>(["requests"]);
    const updated = cached?.find((r) => r.id === "req-002");
    expect(updated?.spaceId).toBe("space-A");
    expect(updated?.startTs).toBe("2026-04-03T10:00:00Z");
  });
});
