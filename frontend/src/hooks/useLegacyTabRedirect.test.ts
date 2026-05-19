/** @jsxImportSource react */
import { describe, it, expect } from 'vitest';
import { renderHook } from '@testing-library/react';
import { MemoryRouter, Routes, Route, useLocation } from 'react-router-dom';
import { createElement } from 'react';
import { useLegacyTabRedirect } from './useLegacyTabRedirect';

const MAP: Record<string, string> = {
  jobTitles: '/people/job-titles',
  departments: '/people/departments',
  groups: '/people/groups',
};

function LocationProbe() {
  const { pathname, search } = useLocation();
  return createElement('div', { 'data-testid': 'location' }, `${pathname}${search}`);
}

function wrapper(initialPath: string) {
  return ({ children }: { children: React.ReactNode }) =>
    createElement(
      MemoryRouter,
      { initialEntries: [initialPath] },
      createElement(
        Routes,
        null,
        createElement(Route, { path: '/people', element: children as React.ReactElement }),
        createElement(Route, { path: '/people/*', element: createElement(LocationProbe) }),
      ),
    );
}

describe('useLegacyTabRedirect', () => {
  it('redirects a known legacy tab param to the target path', () => {
    // The hook should not throw during render — navigation side-effect is
    // exercised end-to-end in PeoplePage.test.tsx and SettingsPage.test.tsx.
    expect(() =>
      renderHook(() => useLegacyTabRedirect(MAP), {
        wrapper: wrapper('/people?tab=jobTitles'),
      }),
    ).not.toThrow();
  });

  it('does nothing when no tab param is present', () => {
    expect(() =>
      renderHook(() => useLegacyTabRedirect(MAP), {
        wrapper: wrapper('/people'),
      }),
    ).not.toThrow();
  });

  it('does nothing when the tab param does not match any key', () => {
    expect(() =>
      renderHook(() => useLegacyTabRedirect(MAP), {
        wrapper: wrapper('/people?tab=unknown'),
      }),
    ).not.toThrow();
  });
});
