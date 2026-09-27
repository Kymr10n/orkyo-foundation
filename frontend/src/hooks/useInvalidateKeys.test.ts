import { describe, it, expect } from 'vitest';
import { renderHook } from '@testing-library/react';
import { createTestQueryClient } from '@foundation/src/test-utils';
import { useInvalidateKeys } from './useInvalidateKeys';

describe('useInvalidateKeys', () => {
  it('invalidates every key it is given', async () => {
    const { spy, wrapper } = createTestQueryClient();
    const { result } = renderHook(() => useInvalidateKeys(['a'], ['b', { id: 1 }]), { wrapper });

    await result.current();

    expect(spy).toHaveBeenCalledWith({ queryKey: ['a'] });
    expect(spy).toHaveBeenCalledWith({ queryKey: ['b', { id: 1 }] });
  });

  it('keeps its identity across renders while the keys keep their content', () => {
    const { wrapper } = createTestQueryClient();
    const { result, rerender } = renderHook(({ id }) => useInvalidateKeys(['site', id]), {
      wrapper,
      initialProps: { id: 's1' },
    });
    const first = result.current;

    rerender({ id: 's1' });
    expect(result.current).toBe(first);

    rerender({ id: 's2' });
    expect(result.current).not.toBe(first);
  });

  it('invalidates the current keys after they change', async () => {
    const { spy, wrapper } = createTestQueryClient();
    const { result, rerender } = renderHook(({ id }) => useInvalidateKeys(['site', id]), {
      wrapper,
      initialProps: { id: 's1' },
    });

    rerender({ id: 's2' });
    await result.current();

    expect(spy).toHaveBeenCalledWith({ queryKey: ['site', 's2'] });
    expect(spy).not.toHaveBeenCalledWith({ queryKey: ['site', 's1'] });
  });
});
