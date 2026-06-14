import { describe, it, expect, vi } from 'vitest';
import { QueryClient } from '@tanstack/react-query';
import { invalidateRequestData } from './invalidate-request-data';

describe('invalidateRequestData', () => {
  it('invalidates both the request lists and the conflicts registry', () => {
    // Every request mutation routes through this helper. Conflicts are derived from request
    // state, so both must refresh together — otherwise the grid's conflict badges go stale.
    const queryClient = new QueryClient();
    const spy = vi.spyOn(queryClient, 'invalidateQueries');

    invalidateRequestData(queryClient);

    expect(spy).toHaveBeenCalledWith({ queryKey: ['requests'] });
    expect(spy).toHaveBeenCalledWith({ queryKey: ['conflicts'] });
    expect(spy).toHaveBeenCalledTimes(2);
  });
});
