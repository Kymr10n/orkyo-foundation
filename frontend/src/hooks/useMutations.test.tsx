/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { renderHook, waitFor } from "@testing-library/react";
import { createCrudHooks } from "./useMutations";
import { createTestQueryWrapper, createTestQueryClientWithSpy } from "@foundation/src/test-utils";

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));
import { toast } from 'sonner';

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
        expect(spy).toHaveBeenCalledWith({ queryKey: ["test-items"], exact: false });
        expect(spy).toHaveBeenCalledWith({ queryKey: ["related-items"], exact: false });
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
        expect(spy).toHaveBeenCalledWith({ queryKey: ["test-items", "site-1"], exact: false });
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
        expect(spy).toHaveBeenCalledWith({ queryKey: ["test-items"], exact: false });
        expect(spy).toHaveBeenCalledWith({ queryKey: ["related-items"], exact: false });
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
        expect(spy).toHaveBeenCalledWith({ queryKey: ["test-items"], exact: false });
        expect(spy).toHaveBeenCalledWith({ queryKey: ["related-items"], exact: false });
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
        expect(spy).toHaveBeenCalledWith({ queryKey: ["test-items"], exact: false });
        // Additional invalidateKeys
        expect(spy).toHaveBeenCalledWith({ queryKey: ["related-items"], exact: false });
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
        expect(spy).toHaveBeenCalledWith({ queryKey: ["simple"], exact: false });
        // Only 1 call — no additional keys
        expect(spy).toHaveBeenCalledTimes(1);
      });
    });
  });

  describe('entityLabel toasts', () => {
    const labeledHooks = createCrudHooks<TestItem, CreateTestItem, UpdateTestItem>({
      queryKey: () => ['labeled-items'] as const,
      queryFn: mockQueryFn,
      createFn: mockCreateFn,
      updateFn: mockUpdateFn,
      deleteFn: mockDeleteFn,
      entityLabel: 'Widget',
    });

    it('fires success toast on create', async () => {
      const { result } = renderHook(() => labeledHooks.useCreate(), {
        wrapper: createTestQueryWrapper(),
      });
      await result.current.mutateAsync({ name: 'New' });
      await waitFor(() => expect(vi.mocked(toast.success)).toHaveBeenCalledWith('Widget created'));
    });

    it('fires error toast on create failure', async () => {
      mockCreateFn.mockRejectedValueOnce(new Error('Conflict'));
      const { result } = renderHook(() => labeledHooks.useCreate(), {
        wrapper: createTestQueryWrapper(),
      });
      await result.current.mutateAsync({ name: 'Fail' }).catch(() => {});
      await waitFor(() =>
        expect(vi.mocked(toast.error)).toHaveBeenCalledWith(
          'Failed to create widget',
          expect.objectContaining({ description: 'Conflict' }),
        ),
      );
    });

    it('fires success toast on update', async () => {
      const { result } = renderHook(() => labeledHooks.useUpdate(), {
        wrapper: createTestQueryWrapper(),
      });
      await result.current.mutateAsync({ id: '1', data: { name: 'Updated' } });
      await waitFor(() => expect(vi.mocked(toast.success)).toHaveBeenCalledWith('Widget updated'));
    });

    it('fires success toast on delete', async () => {
      const { result } = renderHook(() => labeledHooks.useDelete(), {
        wrapper: createTestQueryWrapper(),
      });
      await result.current.mutateAsync('1');
      await waitFor(() => expect(vi.mocked(toast.success)).toHaveBeenCalledWith('Widget deleted'));
    });

    it('fires error toast on delete failure', async () => {
      mockDeleteFn.mockRejectedValueOnce(new Error('Not found'));
      const { result } = renderHook(() => labeledHooks.useDelete(), {
        wrapper: createTestQueryWrapper(),
      });
      await result.current.mutateAsync('bad-id').catch(() => {});
      await waitFor(() =>
        expect(vi.mocked(toast.error)).toHaveBeenCalledWith(
          'Failed to delete widget',
          expect.objectContaining({ description: 'Not found' }),
        ),
      );
    });
  });

  describe('errorMessage helper (non-Error values)', () => {
    it('converts a string rejection to a toast description', async () => {
      const labeledHooks = createCrudHooks<TestItem, CreateTestItem, UpdateTestItem>({
        queryKey: () => ['str-err'] as const,
        queryFn: mockQueryFn,
        createFn: vi.fn().mockRejectedValue('plain string error'),
        entityLabel: 'Thing',
      });
      const { result } = renderHook(() => labeledHooks.useCreate(), {
        wrapper: createTestQueryWrapper(),
      });
      await result.current.mutateAsync({ name: 'x' }).catch(() => {});
      await waitFor(() =>
        expect(vi.mocked(toast.error)).toHaveBeenCalledWith(
          'Failed to create thing',
          expect.objectContaining({ description: 'plain string error' }),
        ),
      );
    });
  });
});
