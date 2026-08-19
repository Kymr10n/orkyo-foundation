/** @jsxImportSource react */
import { describe, it, expect } from 'vitest';
import { renderHook } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import { createElement } from 'react';
import { useActiveTab } from './useActiveTab';

function wrapper(initialPath: string) {
  return ({ children }: { children: React.ReactNode }) =>
    createElement(MemoryRouter, { initialEntries: [initialPath] }, children);
}

describe('useActiveTab', () => {
  it('returns the default when there is no tab segment', () => {
    const { result } = renderHook(() => useActiveTab('criteria'), {
      wrapper: wrapper('/settings'),
    });
    expect(result.current).toBe('criteria');
  });

  it('returns the tab segment when present', () => {
    const { result } = renderHook(() => useActiveTab('criteria'), {
      wrapper: wrapper('/settings/templates'),
    });
    expect(result.current).toBe('templates');
  });

  it('ignores deeper path segments beyond the tab', () => {
    const { result } = renderHook(() => useActiveTab('criteria'), {
      wrapper: wrapper('/settings/templates/extra/more'),
    });
    expect(result.current).toBe('templates');
  });

  it('reads the tab one segment deeper when asked', () => {
    // The class pages address a type before their tab: /stations/<key>/<tab>.
    const { result } = renderHook(() => useActiveTab('instances', 3), {
      wrapper: wrapper('/stations/mill/groups'),
    });
    expect(result.current).toBe('groups');
  });

  it('falls back to the default at depth 3 when the tab is absent', () => {
    const { result } = renderHook(() => useActiveTab('instances', 3), {
      wrapper: wrapper('/stations/mill'),
    });
    expect(result.current).toBe('instances');
  });

  it('works with different base paths and defaults', () => {
    const { result } = renderHook(() => useActiveTab('criteria'), {
      wrapper: wrapper('/settings/organization'),
    });
    expect(result.current).toBe('organization');
  });

  it('returns the default when path has only one segment', () => {
    const { result } = renderHook(() => useActiveTab('list'), {
      wrapper: wrapper('/stations'),
    });
    expect(result.current).toBe('list');
  });
});
