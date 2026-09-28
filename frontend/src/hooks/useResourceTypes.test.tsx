import { describe, it, expect, vi, beforeEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import * as resourceTypesApi from '@foundation/src/lib/api/resource-types-api';
import { createTestQueryClient } from '@foundation/src/test-utils';
import { useDeleteResourceType } from './useResourceTypes';

vi.mock('@foundation/src/lib/api/resource-types-api');

describe('useDeleteResourceType', () => {
  beforeEach(() => vi.clearAllMocks());

  it('refreshes the per-type resource lists as well as the flat list', async () => {
    vi.mocked(resourceTypesApi.deleteResourceType).mockResolvedValue(undefined as never);
    const { spy, wrapper } = createTestQueryClient({ feedback: true });
    const { result } = renderHook(() => useDeleteResourceType(), { wrapper });

    await act(() => result.current.mutateAsync('rt-1'));

    await waitFor(() => {
      expect(spy).toHaveBeenCalledWith({ queryKey: ['resources'], exact: false });
    });
  });
});
