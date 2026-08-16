/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import {
  usePlaceableResources,
  useCreatePlaceableResource,
  useUpdatePlaceableResource,
  useDeletePlaceableResource,
  useMovePlaceableResource,
} from './usePlaceableResources';
import * as resourcesApi from '@foundation/src/lib/api/resources-api';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';
import type { ResourceGeometry } from '@foundation/src/types/geometry';
import { qk } from '@foundation/src/lib/api/query-keys';
import type { ReactNode } from 'react';
import { createFeedbackMutationCache } from '@foundation/src/lib/core/query-client';

vi.mock('@foundation/src/lib/api/resources-api');
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
import { toast } from 'sonner';

// Non-optimistic mutations route toast + invalidation through the meta-driven MutationCache;
// wire it (like production) so those fire in tests.
function makeFeedbackClient() {
  const client: QueryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    mutationCache: createFeedbackMutationCache(() => client),
  });
  return client;
}

function feedbackWrapper(client: QueryClient) {
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
}

function placeable(overrides: Partial<ResourceInfo> = {}): ResourceInfo {
  return {
    id: 'space-1',
    resourceTypeId: 'type-space',
    resourceTypeKey: 'space',
    name: 'Conference Room A',
    code: 'CR-A',
    description: 'Main conference room',
    allocationMode: 'Exclusive',
    baseAvailabilityPercent: 100,
    isActive: true,
    homeSiteId: 'site-1',
    crossSiteAllowed: false,
    isPhysical: true,
    capacity: 1,
    geometry: {
      type: 'rectangle',
      coordinates: [
        { x: 100, y: 100 },
        { x: 300, y: 250 },
      ],
    },
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
    ...overrides,
  };
}

function listResponse(items: ResourceInfo[]) {
  return { data: items, total: items.length, page: 1, pageSize: items.length };
}

describe('usePlaceableResources', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('asks for every placeable resource at the site, whatever its type', async () => {
    // One floorplan holds them all, so the query is type-agnostic — a tenant-defined placeable
    // type appears without any per-type wiring.
    vi.mocked(resourcesApi.getResources).mockResolvedValue(listResponse([placeable()]));

    const { result } = renderHook(() => usePlaceableResources('site-1'), {
      wrapper: feedbackWrapper(makeFeedbackClient()),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(resourcesApi.getResources).toHaveBeenCalledWith({
      hasGeometry: true,
      isActive: true,
      siteId: 'site-1',
    });
  });

  it('orders by code, falling back to name', async () => {
    // The site-scoped space route returned ORDER BY code, name; the generic list orders by name
    // alone, so the ordering moved here rather than being silently lost.
    vi.mocked(resourcesApi.getResources).mockResolvedValue(listResponse([
      placeable({ id: 'b', code: 'B-2', name: 'Zulu' }),
      placeable({ id: 'a', code: 'A-1', name: 'Alpha' }),
      placeable({ id: 'c', code: null, name: 'Bravo' }),
    ]));

    const { result } = renderHook(() => usePlaceableResources('site-1'), {
      wrapper: feedbackWrapper(makeFeedbackClient()),
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.map((r) => r.id)).toEqual(['a', 'b', 'c']);
  });

  it('does not query without a site', () => {
    renderHook(() => usePlaceableResources(null), {
      wrapper: feedbackWrapper(makeFeedbackClient()),
    });
    // No wait: a query with enabled:false is never scheduled.
    expect(resourcesApi.getResources).not.toHaveBeenCalled();
  });

  it('creates through the generic resource endpoint', async () => {
    vi.mocked(resourcesApi.createResource).mockResolvedValue(placeable());
    const { result } = renderHook(() => useCreatePlaceableResource('site-1'), {
      wrapper: feedbackWrapper(makeFeedbackClient()),
    });

    result.current.mutate({
      resourceTypeKey: 'space',
      name: 'New Room',
      allocationMode: 'Exclusive',
      homeSiteId: 'site-1',
      crossSiteAllowed: false,
      isPhysical: false,
    });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(resourcesApi.createResource).toHaveBeenCalledWith(
      expect.objectContaining({ resourceTypeKey: 'space', homeSiteId: 'site-1' }),
    );
    expect(toast.success).toHaveBeenCalledWith('Resource created');
  });

  it('updates by resource id, with no site in the path', async () => {
    vi.mocked(resourcesApi.updateResource).mockResolvedValue(placeable());
    const { result } = renderHook(() => useUpdatePlaceableResource('site-1'), {
      wrapper: feedbackWrapper(makeFeedbackClient()),
    });

    result.current.mutate({ resourceId: 'space-1', data: { name: 'Renamed' } });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(resourcesApi.updateResource).toHaveBeenCalledWith('space-1', { name: 'Renamed' });
  });

  it('sends geometry alone when moving', async () => {
    // The space route re-sent name/code/description/isPhysical on every drag. The generic update
    // writes only what it is given, so a concurrent rename is no longer overwritten by a move.
    vi.mocked(resourcesApi.updateResource).mockResolvedValue(placeable());
    const geometry: ResourceGeometry = {
      type: 'rectangle',
      coordinates: [{ x: 0, y: 0 }, { x: 10, y: 10 }],
    };
    const { result } = renderHook(() => useMovePlaceableResource('site-1'), {
      wrapper: feedbackWrapper(makeFeedbackClient()),
    });

    result.current.mutate({ resourceId: 'space-1', geometry });

    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(resourcesApi.updateResource).toHaveBeenCalledWith('space-1', { geometry });
  });

  it('removes the row immediately on delete', async () => {
    vi.mocked(resourcesApi.deleteResource).mockResolvedValue(undefined);
    const client = makeFeedbackClient();
    client.setQueryData(qk.resources.placeable('site-1'), [placeable(), placeable({ id: 'space-2' })]);

    const { result } = renderHook(() => useDeletePlaceableResource('site-1'), {
      wrapper: feedbackWrapper(client),
    });
    result.current.mutate('space-1');

    await waitFor(() =>
      expect(client.getQueryData<ResourceInfo[]>(qk.resources.placeable('site-1'))).toHaveLength(1),
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it('puts the row back when the delete fails', async () => {
    vi.mocked(resourcesApi.deleteResource).mockRejectedValue(new Error('nope'));
    const client = makeFeedbackClient();
    client.setQueryData(qk.resources.placeable('site-1'), [placeable()]);

    const { result } = renderHook(() => useDeletePlaceableResource('site-1'), {
      wrapper: feedbackWrapper(client),
    });
    result.current.mutate('space-1');

    await waitFor(() => expect(result.current.isError).toBe(true));
    expect(client.getQueryData<ResourceInfo[]>(qk.resources.placeable('site-1'))).toHaveLength(1);
    expect(toast.error).toHaveBeenCalledWith(
      'Failed to delete resource',
      expect.objectContaining({ description: expect.any(String) }),
    );
  });
});
