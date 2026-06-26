import { describe, it, expect, vi } from 'vitest';
import { QueryClient } from '@tanstack/react-query';
import { invalidateRequestData, REQUEST_DERIVED_QUERY_KEYS } from './invalidate-request-data';

describe('invalidateRequestData', () => {
  it('invalidates every request-derived query namespace', () => {
    // Every request/assignment mutation routes through this helper. Asserting against the exported
    // contract (rather than hardcoded keys) keeps the test from drifting from the implementation and
    // auto-covers any namespace added to REQUEST_DERIVED_QUERY_KEYS.
    const queryClient = new QueryClient();
    const spy = vi.spyOn(queryClient, 'invalidateQueries');

    invalidateRequestData(queryClient);

    for (const queryKey of REQUEST_DERIVED_QUERY_KEYS) {
      expect(spy).toHaveBeenCalledWith({ queryKey });
    }
    expect(spy).toHaveBeenCalledTimes(REQUEST_DERIVED_QUERY_KEYS.length);
  });

  it('covers the occupancy grids and insights, not just requests and conflicts', () => {
    // Guards the original gap: a request mutation must also refresh the utilization grids and the
    // insights trend charts (they derive from request + assignment state under different prefixes).
    const keys = REQUEST_DERIVED_QUERY_KEYS.map((k) => k[0]);
    expect(keys).toEqual(
      expect.arrayContaining([
        'requests',
        'conflicts',
        'utilization-by-resource',
        'resource-assignments-by-type',
        'insights',
      ]),
    );
  });
});
