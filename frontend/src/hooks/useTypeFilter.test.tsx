import { describe, it, expect, beforeEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router';
import type { ReactNode } from 'react';
import { useTypeFilter } from './useTypeFilter';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';

const MILL = { key: 'mill' } as ResourceTypeInfo;
const DRILL = { key: 'drill' } as ResourceTypeInfo;
const PRESS = { key: 'press' } as ResourceTypeInfo;
const STATIONS = [MILL, DRILL];

function wrapper(initialPath = '/') {
  return ({ children }: { children: ReactNode }) => (
    <MemoryRouter initialEntries={[initialPath]}>{children}</MemoryRouter>
  );
}

/** Renders the hook alongside the live location, so a write can be observed in the URL. */
function renderFilter(
  available: ResourceTypeInfo[] = STATIONS,
  initialPath = '/',
  paramName = 'stationTypes',
) {
  return renderHook(
    () => ({ filter: useTypeFilter(paramName, available), location: useLocation() }),
    { wrapper: wrapper(initialPath) },
  );
}

beforeEach(() => localStorage.clear());

describe('useTypeFilter', () => {
  it('selects every type when nothing is stored or in the URL', () => {
    const { result } = renderFilter();

    expect(result.current.filter[0]).toEqual(['mill', 'drill']);
  });

  it('reads the selection from its own URL parameter', () => {
    const { result } = renderFilter(STATIONS, '/?stationTypes=drill');

    expect(result.current.filter[0]).toEqual(['drill']);
  });

  it('ignores a parameter belonging to the other tab', () => {
    // Both grid tabs live on one page; a shared key would let one tab reset the other.
    const { result } = renderFilter(STATIONS, '/?assetTypes=person');

    expect(result.current.filter[0]).toEqual(['mill', 'drill']);
  });

  it('drops a key the tenant has since deactivated', () => {
    const { result } = renderFilter(STATIONS, '/?stationTypes=drill,vanished');

    expect(result.current.filter[0]).toEqual(['drill']);
  });

  it('falls back to everything when no known key survives', () => {
    const { result } = renderFilter(STATIONS, '/?stationTypes=vanished');

    expect(result.current.filter[0]).toEqual(['mill', 'drill']);
  });

  it('writes a partial selection to the URL', () => {
    const { result } = renderFilter();

    act(() => result.current.filter[1](['mill']));

    expect(result.current.location.search).toBe('?stationTypes=mill');
    expect(result.current.filter[0]).toEqual(['mill']);
  });

  it('records "everything" as absence, not as a pinned list', () => {
    const { result } = renderFilter(STATIONS, '/?stationTypes=mill');

    act(() => result.current.filter[1](['mill', 'drill']));

    expect(result.current.location.search).toBe('');
    expect(localStorage.getItem('orkyo.typeFilter.stationTypes')).toBeNull();
  });

  it('does not exclude a type defined after the last selection', () => {
    // The reason "everything" is stored as absence: a pinned list would silently withhold a type
    // the tenant added later.
    const first = renderFilter(STATIONS);
    act(() => first.result.current.filter[1](['mill', 'drill']));
    first.unmount();

    const { result } = renderFilter([MILL, DRILL, PRESS]);
    expect(result.current.filter[0]).toEqual(['mill', 'drill', 'press']);
  });

  it('remembers a partial selection across visits when the URL is silent', () => {
    const first = renderFilter();
    act(() => first.result.current.filter[1](['drill']));
    first.unmount();

    const { result } = renderFilter(STATIONS);
    expect(result.current.filter[0]).toEqual(['drill']);
  });

  it('lets the URL win over what was stored', () => {
    localStorage.setItem('orkyo.typeFilter.stationTypes', JSON.stringify(['drill']));
    const { result } = renderFilter(STATIONS, '/?stationTypes=mill');

    expect(result.current.filter[0]).toEqual(['mill']);
  });

  it('survives unreadable storage', () => {
    localStorage.setItem('orkyo.typeFilter.stationTypes', 'not json');
    const { result } = renderFilter();

    expect(result.current.filter[0]).toEqual(['mill', 'drill']);
  });
});
