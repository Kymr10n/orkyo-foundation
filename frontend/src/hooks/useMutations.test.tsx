/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { createCrudHooks } from "./useMutations";
import { createTestQueryWrapper, createTestQueryClientWithSpy } from "@foundation/src/test-utils";

interface TestItem {
  id: string;
  name: string;
}

interface CreateTestItem {
  name: string;
}

interface UpdateTestItem {
  name?: string;
}

const mockItems: TestItem[] = [
  { id: "1", name: "Item A" },
  { id: "2", name: "Item B" },
];

const mockQueryFn = vi.fn<() => Promise<TestItem[]>>();
const mockCreateFn = vi.fn<(data: CreateTestItem) => Promise<TestItem>>();
const mockUpdateFn = vi.fn<(id: string, data: UpdateTestItem) => Promise<TestItem>>();
const mockDeleteFn = vi.fn<(id: string) => Promise<void>>();

const hooks = createCrudHooks<TestItem, CreateTestItem, UpdateTestItem>({
  queryKey: () => ["test-items"] as const,
  queryFn: mockQueryFn,
  createFn: mockCreateFn,
  updateFn: mockUpdateFn,
  deleteFn: mockDeleteFn,
  invalidateKeys: () => [["related-items"]],
});

describe("createCrudHooks", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockQueryFn.mockResolvedValue(mockItems);
    mockCreateFn.mockResolvedValue({ id: "3", name: "Item C" });
    mockUpdateFn.mockResolvedValue({ id: "1", name: "Updated" });
    mockDeleteFn.mockResolvedValue(undefined);
  });

  describe("useQuery", () => {
    it("fetches data when enabled is explicitly true", async () => {
      const { result } = renderHook(() => hooks.useQuery(undefined, { enabled: true }), {
        wrapper: createTestQueryWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(result.current.data).toEqual(mockItems);
      expect(mockQueryFn).toHaveBeenCalled();
    });

    it("is disabled by default when TParams is undefined", () => {
      const { result } = renderHook(() => hooks.useQuery(), {
        wrapper: createTestQueryWrapper(),
      });

      expect(result.current.data).toBeUndefined();
      expect(mockQueryFn).not.toHaveBeenCalled();
    });

    it("disables fetch when params is null", () => {
      // Create parameterized hooks to test the enabled guard
      const paramHooks = createCrudHooks<TestItem, CreateTestItem, UpdateTestItem, string | null>({
        queryKey: (p) => ["test-items", p] as const,
        queryFn: mockQueryFn,
      });

      const { result } = renderHook(() => paramHooks.useQuery(null), {
        wrapper: createTestQueryWrapper(),
      });

      expect(result.current.data).toBeUndefined();
      expect(mockQueryFn).not.toHaveBeenCalled();
    });

    it("passes params to queryFn", async () => {
      const paramHooks = createCrudHooks<TestItem, CreateTestItem, UpdateTestItem, string>({
        queryKey: (p) => ["test-items", p] as const,
        queryFn: mockQueryFn,
      });

      const { result } = renderHook(() => paramHooks.useQuery("site-1"), {
        wrapper: createTestQueryWrapper(),
      });

      await waitFor(() => expect(result.current.isSuccess).toBe(true));
      expect(mockQueryFn).toHaveBeenCalledWith("site-1");
    });

    it("handles fetch errors", async () => {
      mockQueryFn.mockRejectedValue(new Error("Network error"));

      const { result } = renderHook(() => hooks.useQuery(undefined, { enabled: true }), {
        wrapper: createTestQueryWrapper(),
      });

      await waitFor(() => expect(result.current.isError).toBe(true));
      expect(result.current.error).toEqual(new Error("Network error"));
    });
  });

  describe("useCreate", () => {
    it("calls createFn and invalidates caches", async () => {
      const { spy, wrapper } = createTestQueryClientWithSpy();

      const { result } = renderHook(() => hooks.useCreate(), { wrapper });

      await result.current.mutateAsync({ name: "Item C" });

      expect(mockCreateFn).toHaveBeenCalledWith({ name: "Item C" }, undefined);
      await waitFor(() => {
        expect(spy).toHaveBeenCalledWith({ queryKey: ["test-items"] });
        expect(spy).toHaveBeenCalledWith({ queryKey: ["related-items"] });
      });
    });

    it("passes params to createFn", async () => {
      const paramHooks = createCrudHooks<TestItem, CreateTestItem, UpdateTestItem, string>({
        queryKey: (p) => ["test-items", p] as const,
        queryFn: mockQueryFn,
        createFn: mockCreateFn,
      });

      const { spy, wrapper } = createTestQueryClientWithSpy();
      const { result } = renderHook(() => paramHooks.useCreate("site-1"), { wrapper });

      await result.current.mutateAsync({ name: "New" });

      expect(mockCreateFn).toHaveBeenCalledWith({ name: "New" }, "site-1");
      await waitFor(() => {
        expect(spy).toHaveBeenCalledWith({ queryKey: ["test-items", "site-1"] });
      });
    });
  });

  describe("useUpdate", () => {
    it("calls updateFn and invalidates caches", async () => {
      const { spy, wrapper } = createTestQueryClientWithSpy();

      const { result } = renderHook(() => hooks.useUpdate(), { wrapper });

      await result.current.mutateAsync({ id: "1", data: { name: "Updated" } });

      expect(mockUpdateFn).toHaveBeenCalledWith("1", { name: "Updated" }, undefined);
      await waitFor(() => {
        expect(spy).toHaveBeenCalledWith({ queryKey: ["test-items"] });
        expect(spy).toHaveBeenCalledWith({ queryKey: ["related-items"] });
      });
    });
  });

  describe("useDelete", () => {
    it("calls deleteFn and invalidates caches", async () => {
      const { spy, wrapper } = createTestQueryClientWithSpy();

      const { result } = renderHook(() => hooks.useDelete(), { wrapper });

      await result.current.mutateAsync("1");

      expect(mockDeleteFn).toHaveBeenCalledWith("1", undefined);
      await waitFor(() => {
        expect(spy).toHaveBeenCalledWith({ queryKey: ["test-items"] });
        expect(spy).toHaveBeenCalledWith({ queryKey: ["related-items"] });
      });
    });
  });

  describe("invalidation", () => {
    it("invalidates both primary key and additional keys", async () => {
      const { spy, wrapper } = createTestQueryClientWithSpy();

      const { result } = renderHook(() => hooks.useCreate(), { wrapper });
      await result.current.mutateAsync({ name: "test" });

      await waitFor(() => {
        // Primary key
        expect(spy).toHaveBeenCalledWith({ queryKey: ["test-items"] });
        // Additional invalidateKeys
        expect(spy).toHaveBeenCalledWith({ queryKey: ["related-items"] });
      });
    });

    it("works without invalidateKeys config", async () => {
      const simpleHooks = createCrudHooks<TestItem, CreateTestItem, UpdateTestItem>({
        queryKey: () => ["simple"] as const,
        queryFn: mockQueryFn,
        createFn: mockCreateFn,
      });

      const { spy, wrapper } = createTestQueryClientWithSpy();
      const { result } = renderHook(() => simpleHooks.useCreate(), { wrapper });

      await result.current.mutateAsync({ name: "test" });

      await waitFor(() => {
        expect(spy).toHaveBeenCalledWith({ queryKey: ["simple"] });
        // Only 1 call — no additional keys
        expect(spy).toHaveBeenCalledTimes(1);
      });
    });
  });
});
