/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { createElement, type ReactNode } from 'react';
import {
  useAllSharedListInstances,
  useCreateListColumn,
  useCreateListDefinition,
  useCreateSharedListInstance,
  useDeleteListColumn,
  useDeleteListDefinition,
  useDeleteSharedListInstance,
  useListDefinition,
  useListDefinitions,
  useSharedListInstances,
  useUpdateListColumn,
  useUpdateListDefinition,
  useUpdateSharedListInstance,
} from './useListDefinitions';

const api = {
  getListDefinitions: vi.fn(),
  getListDefinition: vi.fn(),
  createListDefinition: vi.fn(),
  updateListDefinition: vi.fn(),
  deleteListDefinition: vi.fn(),
  createListColumn: vi.fn(),
  updateListColumn: vi.fn(),
  deleteListColumn: vi.fn(),
  getSharedListInstances: vi.fn(),
  createSharedListInstance: vi.fn(),
  updateSharedListInstance: vi.fn(),
  deleteSharedListInstance: vi.fn(),
};

vi.mock('@foundation/src/lib/api/lists-api', () => ({
  getListDefinitions: (...a: unknown[]) => api.getListDefinitions(...a),
  getListDefinition: (...a: unknown[]) => api.getListDefinition(...a),
  createListDefinition: (...a: unknown[]) => api.createListDefinition(...a),
  updateListDefinition: (...a: unknown[]) => api.updateListDefinition(...a),
  deleteListDefinition: (...a: unknown[]) => api.deleteListDefinition(...a),
  createListColumn: (...a: unknown[]) => api.createListColumn(...a),
  updateListColumn: (...a: unknown[]) => api.updateListColumn(...a),
  deleteListColumn: (...a: unknown[]) => api.deleteListColumn(...a),
  getSharedListInstances: (...a: unknown[]) => api.getSharedListInstances(...a),
  createSharedListInstance: (...a: unknown[]) => api.createSharedListInstance(...a),
  updateSharedListInstance: (...a: unknown[]) => api.updateSharedListInstance(...a),
  deleteSharedListInstance: (...a: unknown[]) => api.deleteSharedListInstance(...a),
}));

const DEF = 'def-1';
const COL = 'col-1';
const INST = 'inst-1';

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return createElement(QueryClientProvider, { client }, children);
}

/**
 * The data layer behind the list-definitions admin. The mutations all declare the same broad
 * invalidation on purpose — a column change reshapes every row of every instance built from the
 * definition — and these pin that each one actually reaches its endpoint with what it was given.
 */
describe('useListDefinitions', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.getListDefinitions.mockResolvedValue([]);
    api.getListDefinition.mockResolvedValue({ id: DEF });
    api.getSharedListInstances.mockResolvedValue([]);
    Object.values(api).forEach((fn) => {
      if (fn.getMockImplementation() === undefined) fn.mockResolvedValue({});
    });
  });

  it('asks for active definitions by default', async () => {
    renderHook(() => useListDefinitions(), { wrapper });

    await waitFor(() => expect(api.getListDefinitions).toHaveBeenCalledWith(false, undefined));
  });

  it('passes the scope filter through, and keys on it', async () => {
    // Organization-scoped definitions are a different answer from every definition, so the two
    // must not share a cache entry.
    const { rerender } = renderHook(
      ({ scope }: { scope?: 'organization' | 'resource' }) => useListDefinitions(true, scope),
      { wrapper, initialProps: { scope: 'organization' as 'organization' | 'resource' | undefined } },
    );
    await waitFor(() => expect(api.getListDefinitions).toHaveBeenCalledWith(true, 'organization'));

    rerender({ scope: 'resource' });
    await waitFor(() => expect(api.getListDefinitions).toHaveBeenCalledWith(true, 'resource'));
  });

  it('does not fetch one definition until an id exists', async () => {
    const { result, rerender } = renderHook(
      ({ id }: { id: string | null }) => useListDefinition(id),
      { wrapper, initialProps: { id: null as string | null } },
    );
    // Disabled, not loading: there is nothing to fetch, which is an ordinary state rather than
    // a spinner that never resolves.
    expect(api.getListDefinition).not.toHaveBeenCalled();
    expect(result.current.fetchStatus).toBe('idle');
    expect(result.current.data).toBeUndefined();

    rerender({ id: DEF });
    await waitFor(() => expect(api.getListDefinition).toHaveBeenCalledWith(DEF));
  });

  it('does not fetch shared instances until a definition is chosen', async () => {
    const { rerender } = renderHook(({ id }: { id: string | null }) => useSharedListInstances(id), {
      wrapper,
      initialProps: { id: null as string | null },
    });
    expect(api.getSharedListInstances).not.toHaveBeenCalled();

    rerender({ id: DEF });
    await waitFor(() => expect(api.getSharedListInstances).toHaveBeenCalledWith(DEF));
  });

  it('creates, updates and deletes a definition', async () => {
    const create = renderHook(() => useCreateListDefinition(), { wrapper });
    await act(() => create.result.current.mutateAsync({ name: 'Departments' }));
    expect(api.createListDefinition).toHaveBeenCalledWith({ name: 'Departments' });

    const update = renderHook(() => useUpdateListDefinition(), { wrapper });
    await act(() =>
      update.result.current.mutateAsync({ definitionId: DEF, request: { name: 'Units' } }),
    );
    expect(api.updateListDefinition).toHaveBeenCalledWith(DEF, { name: 'Units' });

    const remove = renderHook(() => useDeleteListDefinition(), { wrapper });
    await act(() => remove.result.current.mutateAsync(DEF));
    expect(api.deleteListDefinition).toHaveBeenCalledWith(DEF);
  });

  it('binds column mutations to the definition they were created for', async () => {
    const create = renderHook(() => useCreateListColumn(DEF), { wrapper });
    await act(() =>
      create.result.current.mutateAsync({ key: 'name', label: 'Name', dataType: 'text' }),
    );
    expect(api.createListColumn).toHaveBeenCalledWith(DEF, {
      key: 'name',
      label: 'Name',
      dataType: 'text',
    });

    const update = renderHook(() => useUpdateListColumn(DEF), { wrapper });
    await act(() =>
      update.result.current.mutateAsync({ columnId: COL, request: { label: 'Renamed' } }),
    );
    expect(api.updateListColumn).toHaveBeenCalledWith(DEF, COL, { label: 'Renamed' });

    const remove = renderHook(() => useDeleteListColumn(DEF), { wrapper });
    await act(() => remove.result.current.mutateAsync(COL));
    expect(api.deleteListColumn).toHaveBeenCalledWith(DEF, COL);
  });

  it('binds instance mutations to their definition', async () => {
    const create = renderHook(() => useCreateSharedListInstance(DEF), { wrapper });
    await act(() => create.result.current.mutateAsync({ name: 'Departments' }));
    expect(api.createSharedListInstance).toHaveBeenCalledWith(DEF, { name: 'Departments' });

    const update = renderHook(() => useUpdateSharedListInstance(DEF), { wrapper });
    await act(() =>
      update.result.current.mutateAsync({ instanceId: INST, request: { name: 'Units' } }),
    );
    expect(api.updateSharedListInstance).toHaveBeenCalledWith(DEF, INST, { name: 'Units' });

    const remove = renderHook(() => useDeleteSharedListInstance(DEF), { wrapper });
    await act(() => remove.result.current.mutateAsync(INST));
    expect(api.deleteSharedListInstance).toHaveBeenCalledWith(DEF, INST);
  });

  it('gathers every shared instance with the definition that names it', async () => {
    api.getListDefinitions.mockResolvedValue([
      { id: 'd1', name: 'Departments' },
      { id: 'd2', name: 'Job Titles' },
    ]);
    api.getSharedListInstances.mockImplementation(async (id: string) =>
      id === 'd1' ? [{ id: 'i1', name: 'Departments' }] : [{ id: 'i2', name: 'Job Titles' }],
    );

    const { result } = renderHook(() => useAllSharedListInstances(), { wrapper });

    await waitFor(() => expect(result.current).toHaveLength(2));
    expect(result.current.map((r) => r.definitionName)).toEqual(['Departments', 'Job Titles']);
    expect(result.current.map((r) => r.instance.id)).toEqual(['i1', 'i2']);
  });
});
