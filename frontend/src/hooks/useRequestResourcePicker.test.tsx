import { describe, it, expect, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { createTestQueryClient } from '@foundation/src/test-utils';
import { pagedResult } from '@foundation/src/test-utils/paged-result';
import { getResources, type ResourceInfo } from '@foundation/src/lib/api/resources-api';
import { useResourceOptions } from './useRequestResourcePicker';
import { useResourcesOfType } from './useResources';

vi.mock('@foundation/src/lib/api/resources-api', () => ({ getResources: vi.fn() }));

const active = { id: 'r-active', name: 'Mill 1', isActive: true } as ResourceInfo;
const retired = { id: 'r-retired', name: 'Mill 0', isActive: false } as ResourceInfo;

describe('useResourceOptions', () => {
  it('does not share a cache entry with the unfiltered list of the same type and site', async () => {
    vi.mocked(getResources).mockImplementation(async (filter) =>
      pagedResult(filter?.isActive ? [active] : [active, retired]),
    );
    const { wrapper } = createTestQueryClient();

    const list = renderHook(() => useResourcesOfType('mill', 'site-1'), { wrapper });
    await waitFor(() => expect(list.result.current.data?.items).toHaveLength(2));
    const options = renderHook(() => useResourceOptions('mill', 'site-1'), { wrapper });

    await waitFor(() => expect(options.result.current.data?.items).toEqual([active]));
    expect(getResources).toHaveBeenCalledWith({ resourceTypeKey: 'mill', isActive: true, siteId: 'site-1' });
    // The active-only fetch must not overwrite the list that shows retired resources too.
    list.rerender();
    expect(list.result.current.data?.items).toEqual([active, retired]);
  });
});
