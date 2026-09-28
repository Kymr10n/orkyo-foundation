/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import * as requestApi from '@foundation/src/lib/api/request-api';
import { createTestQueryWrapper } from '@foundation/src/test-utils';
import { useScheduleRequestNames } from './useResourceSchedule';
import { useRequests } from './useRequests';

vi.mock('@foundation/src/lib/api/request-api');

describe('useScheduleRequestNames', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(requestApi.getRequests).mockResolvedValue([]);
  });

  it('does not share a cache entry with the unscoped request list', async () => {
    const wrapper = createTestQueryWrapper();
    const { result } = renderHook(
      () => ({ names: useScheduleRequestNames(true), list: useRequests(null) }),
      { wrapper },
    );

    await waitFor(() => expect(result.current.names.isSuccess).toBe(true));
    await waitFor(() => expect(result.current.list.isSuccess).toBe(true));
    // One fetch without requirements (names), one with (the list) — not one poisoning the other.
    expect(requestApi.getRequests).toHaveBeenCalledWith();
    expect(requestApi.getRequests).toHaveBeenCalledWith(true, undefined);
  });
});
