/** @jsxImportSource react */
import { describe, it, expect } from 'vitest';
import { renderHook } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { createElement } from 'react';
import { useActiveTab } from './useActiveTab';

function wrapper(initialPath: string) {
  return ({ children }: { children: React.ReactNode }) =>
    createElement(MemoryRouter, { initialEntries: [initialPath] }, children);
}

describe('useActiveTab', () => {
  it('returns the default when there is no tab segment', () => {
    const { result } = renderHook(() => useActiveTab('floorplan'), {
      wrapper: wrapper('/spaces'),
    });
    expect(result.current).toBe('floorplan');
  });

  it('returns the tab segment when present', () => {
    const { result } = renderHook(() => useActiveTab('floorplan'), {
      wrapper: wrapper('/spaces/groups'),
    });
    expect(result.current).toBe('groups');
  });

  it('ignores deeper path segments beyond the tab', () => {
    const { result } = renderHook(() => useActiveTab('floorplan'), {
      wrapper: wrapper('/spaces/floorplan/extra/more'),
    });
    expect(result.current).toBe('floorplan');
  });

  it('works with different base paths and defaults', () => {
    const { result } = renderHook(() => useActiveTab('criteria'), {
      wrapper: wrapper('/settings/organization'),
    });
    expect(result.current).toBe('organization');
  });

  it('returns the default when path has only one segment', () => {
    const { result } = renderHook(() => useActiveTab('list'), {
      wrapper: wrapper('/people'),
    });
    expect(result.current).toBe('list');
  });
});
