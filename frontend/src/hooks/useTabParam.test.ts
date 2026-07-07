/** @jsxImportSource react */
import { describe, it, expect } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { MemoryRouter, useSearchParams } from 'react-router-dom';
import { createElement } from 'react';
import { useTabParam } from './useTabParam';

function wrapper(initialPath: string) {
  return ({ children }: { children: React.ReactNode }) =>
    createElement(MemoryRouter, { initialEntries: [initialPath] }, children);
}

describe('useTabParam', () => {
  it('returns the default when ?tab= is absent', () => {
    const { result } = renderHook(() => useTabParam('profile'), {
      wrapper: wrapper('/account'),
    });
    expect(result.current[0]).toBe('profile');
  });

  it('returns the tab from the query param when present', () => {
    const { result } = renderHook(() => useTabParam('profile'), {
      wrapper: wrapper('/account?tab=security'),
    });
    expect(result.current[0]).toBe('security');
  });

  it('setTab updates the derived active tab', () => {
    const { result } = renderHook(() => useTabParam('calendar'), {
      wrapper: wrapper('/utilization'),
    });
    expect(result.current[0]).toBe('calendar');
    act(() => {
      result.current[1]('people');
    });
    expect(result.current[0]).toBe('people');
  });

  it('setTab preserves other query params', () => {
    const { result } = renderHook(
      () => {
        const [params] = useSearchParams();
        const tab = useTabParam('calendar');
        return { tab, params };
      },
      { wrapper: wrapper('/utilization?site=abc') },
    );
    act(() => {
      result.current.tab[1]('people');
    });
    expect(result.current.tab[0]).toBe('people');
    expect(result.current.params.get('site')).toBe('abc');
    expect(result.current.params.get('tab')).toBe('people');
  });
});
