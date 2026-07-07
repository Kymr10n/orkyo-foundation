/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, act, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useRequests, useScheduleRequest } from "./useUtilization";
import { createTestQueryWrapper } from "@foundation/src/test-utils";
import type { Request } from "@foundation/src/types/requests";

vi.mock("@foundation/src/lib/api/utilization-api");

import * as utilizationApi from "@foundation/src/lib/api/utilization-api";
import { spaceAssignment } from '@foundation/src/test-utils/request-fixtures';

// ---------------------------------------------------------------------------
// Shared mock data
// ---------------------------------------------------------------------------

const mockRequest: Request = {
  id: "req-001",
  name: "Deep-Sea Survey",
  description: "Seismic scan of sector 7",
  assignments: [spaceAssignment('space-A')],
  startTs: "2026-04-01T08:00:00Z",
  endTs: "2026-04-01T10:00:00Z",
  earliestStartTs: null,
  latestEndTs: null,
  minimalDurationValue: 90,
  minimalDurationUnit: "minutes",
  actualDurationValue: 120,
  actualDurationUnit: "minutes",
  durationMin: 90,
  status: "new",
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
  assignments: [],
  startTs: null,
  endTs: null,
  earliestStartTs: "2026-04-02T00:00:00Z",
  latestEndTs: "2026-04-05T23:59:59Z",
  minimalDurationValue: 2,
  minimalDurationUnit: "hours",
  actualDurationValue: null,
  actualDurationUnit: null,
  durationMin: 120,
  status: "new",
  requirements: [],
  schedulingSettingsApply: true,
  planningMode: "leaf",
  sortOrder: 0,
  createdAt: "2026-03-21T11:00:00Z",
  updatedAt: "2026-03-21T11:00:00Z",
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

  it("fetches requests via getAllRequests", async () => {
    vi.mocked(utilizationApi.getAllRequests).mockResolvedValue([
      mockRequest,
      mockRequest2,
    ]);

    const { result } = renderHook(() => useRequests(), {
      wrapper: createTestQueryWrapper(),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    expect(utilizationApi.getAllRequests).toHaveBeenCalledTimes(1);
  });

  it("returns data on success", async () => {
    vi.mocked(utilizationApi.getAllRequests).mockResolvedValue([
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
    vi.mocked(utilizationApi.getAllRequests).mockReturnValue(
      new Promise(() => {}) // never resolves
    );

    const { result } = renderHook(() => useRequests(), {
      wrapper: createTestQueryWrapper(),
    });

    expect(result.current.isLoading).toBe(true);
    expect(result.current.data).toBeUndefined();
  });

  it("exposes error state when fetch fails", async () => {
    vi.mocked(utilizationApi.getAllRequests).mockRejectedValue(
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
// useScheduleRequest
// ---------------------------------------------------------------------------

describe("useScheduleRequest", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("calls scheduleRequest with the correct requestId and data", async () => {
    const updatedRequest: Request = {
      ...mockRequest,
      assignments: [spaceAssignment('space-B')],
      startTs: "2026-04-02T09:00:00Z",
      endTs: "2026-04-02T11:00:00Z",
    };

    vi.mocked(utilizationApi.scheduleRequest).mockResolvedValue(updatedRequest);

    const { queryClient, wrapper } = createClientAndWrapper();
    queryClient.setQueryData<Request[]>(["requests", "scheduled"], [mockRequest]);

    const { result } = renderHook(() => useScheduleRequest(), { wrapper });

    const scheduleData: utilizationApi.ScheduleRequestData = {
      resourceId: "space-B",
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
    queryClient.setQueryData<Request[]>(["requests", "scheduled"], [mockRequest]);

    const { result } = renderHook(() => useScheduleRequest(), { wrapper });

    const scheduleData: utilizationApi.ScheduleRequestData = {
      resourceId: "space-B",
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
      const cached = queryClient.getQueryData<Request[]>(["requests", "scheduled"]);
      const optimistic = cached?.find((r) => r.id === "req-001");
      expect(optimistic?.assignments).toEqual(
        expect.arrayContaining([expect.objectContaining({ resourceId: 'space-B', resourceTypeKey: 'space' })])
      );
      expect(optimistic?.startTs).toBe("2026-04-02T09:00:00Z");
      expect(optimistic?.endTs).toBe("2026-04-02T11:00:00Z");
    });

    // Fields not included in the mutation should be preserved
    const cached = queryClient.getQueryData<Request[]>(["requests", "scheduled"]);
    const optimistic = cached?.find((r) => r.id === "req-001");
    expect(optimistic?.name).toBe("Deep-Sea Survey");
    expect(optimistic?.status).toBe("new");

    // Let the mutation complete
    resolve({
      ...mockRequest,
      assignments: [spaceAssignment('space-B')],
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
    queryClient.setQueryData<Request[]>(["requests", "scheduled"], [mockRequest]);

    const { result } = renderHook(() => useScheduleRequest(), { wrapper });

    await act(async () => {
      try {
        await result.current.mutateAsync({
          requestId: "req-001",
          data: { resourceId: "space-B" },
        });
      } catch {
        // expected
      }
    });

    await waitFor(() => expect(result.current.isError).toBe(true));

    // Cache should be rolled back to the original snapshot
    const cached = queryClient.getQueryData<Request[]>(["requests", "scheduled"]);
    const restored = cached?.find((r) => r.id === "req-001");
    expect(restored?.assignments).toEqual(mockRequest.assignments); // original value (same object reference)
    expect(restored?.startTs).toBe("2026-04-01T08:00:00Z"); // original value
  });

  it("merges server response into cache on success", async () => {
    // The server may return extra computed fields (e.g. actualDurationValue)
    const serverResponse: Request = {
      ...mockRequest,
      assignments: [spaceAssignment('space-B')],
      startTs: "2026-04-02T09:00:00Z",
      endTs: "2026-04-02T11:30:00Z",
      actualDurationValue: 150,
      actualDurationUnit: "minutes",
    };

    vi.mocked(utilizationApi.scheduleRequest).mockResolvedValue(serverResponse);

    const { queryClient, wrapper } = createClientAndWrapper();
    queryClient.setQueryData<Request[]>(["requests", "scheduled"], [mockRequest, mockRequest2]);

    const { result } = renderHook(() => useScheduleRequest(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({
        requestId: "req-001",
        data: {
          resourceId: "space-B",
          startTs: "2026-04-02T09:00:00Z",
          endTs: "2026-04-02T11:30:00Z",
        },
      });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const cached = queryClient.getQueryData<Request[]>(["requests", "scheduled"]);

    // Updated entry should have server-confirmed values
    const updated = cached?.find((r) => r.id === "req-001");
    expect(updated?.assignments).toEqual(
      expect.arrayContaining([expect.objectContaining({ resourceId: 'space-B', resourceTypeKey: 'space' })])
    );
    expect(updated?.endTs).toBe("2026-04-02T11:30:00Z");
    expect(updated?.actualDurationValue).toBe(150);
    expect(updated?.actualDurationUnit).toBe("minutes");

    // Unrelated entry should be unchanged
    const untouched = cached?.find((r) => r.id === "req-002");
    expect(untouched?.assignments).toEqual([]);
  });

  it("unscheduled request: schedules into a space (resourceId was null)", async () => {
    const serverResponse: Request = {
      ...mockRequest2,
      assignments: [spaceAssignment('space-A')],
      startTs: "2026-04-03T10:00:00Z",
      endTs: "2026-04-03T12:00:00Z",
    };

    vi.mocked(utilizationApi.scheduleRequest).mockResolvedValue(serverResponse);

    const { queryClient, wrapper } = createClientAndWrapper();
    queryClient.setQueryData<Request[]>(["requests", "scheduled"], [mockRequest2]);

    const { result } = renderHook(() => useScheduleRequest(), { wrapper });

    await act(async () => {
      await result.current.mutateAsync({
        requestId: "req-002",
        data: {
          resourceId: "space-A",
          startTs: "2026-04-03T10:00:00Z",
          endTs: "2026-04-03T12:00:00Z",
        },
      });
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const cached = queryClient.getQueryData<Request[]>(["requests", "scheduled"]);
    const updated = cached?.find((r) => r.id === "req-002");
    expect(updated?.assignments).toEqual(
      expect.arrayContaining([expect.objectContaining({ resourceId: 'space-A', resourceTypeKey: 'space' })])
    );
    expect(updated?.startTs).toBe("2026-04-03T10:00:00Z");
  });
});
