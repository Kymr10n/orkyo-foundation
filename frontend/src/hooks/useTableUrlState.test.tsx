import { describe, expect, it, vi } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { MemoryRouter, useSearchParams } from 'react-router';
import type { ReactNode } from 'react';
import type { ColumnDef } from '@tanstack/react-table';
import { useTableUrlState } from './useTableUrlState';

interface Row {
  name: string;
  status: string;
  createdAt: string;
  size: number;
}

const columns: ColumnDef<Row>[] = [
  { accessorKey: 'name', header: 'Name', meta: { filter: { type: 'text' } } },
  { accessorKey: 'status', header: 'Status', meta: { filter: { type: 'enum' } } },
  { accessorKey: 'createdAt', header: 'Created', meta: { filter: { type: 'date' } } },
  { accessorKey: 'size', header: 'Size', meta: { filter: { type: 'number' } } },
  { id: 'actions', header: 'Actions' },
];

const wrapperWith = (initialEntry: string) =>
  function Wrapper({ children }: { children: ReactNode }) {
    return <MemoryRouter initialEntries={[initialEntry]}>{children}</MemoryRouter>;
  };

/** Renders the hook plus a searchParams probe so assertions can read the URL. */
const renderUrlState = (initialEntry = '/') =>
  renderHook(
    () => {
      const table = useTableUrlState('people', columns);
      const [searchParams] = useSearchParams();
      return { table, searchParams };
    },
    { wrapper: wrapperWith(initialEntry) },
  );

describe('useTableUrlState', () => {
  it('round-trips sort and every filter type through the URL', () => {
    vi.useFakeTimers();
    const { result } = renderUrlState();

    act(() => {
      result.current.table.onSortingChange([{ id: 'name', desc: true }]);
    });
    act(() => {
      result.current.table.onColumnFiltersChange([
        { id: 'status', value: ['active', 'pending'] },
        { id: 'createdAt', value: ['2026-01-01', undefined] },
        { id: 'size', value: [undefined, '10'] },
      ]);
    });

    expect(result.current.searchParams.get('people_s')).toBe('name.desc');
    expect(result.current.searchParams.get('people_f_status')).toBe('active~pending');
    expect(result.current.searchParams.get('people_f_createdAt')).toBe('2026-01-01..');
    expect(result.current.searchParams.get('people_f_size')).toBe('..10');

    // What was written must decode back to the same state.
    expect(result.current.table.sorting).toEqual([{ id: 'name', desc: true }]);
    expect(result.current.table.columnFilters).toEqual([
      { id: 'status', value: ['active', 'pending'] },
      { id: 'createdAt', value: ['2026-01-01', undefined] },
      { id: 'size', value: [undefined, '10'] },
    ]);
    vi.useRealTimers();
  });

  it('debounces text-filter URL writes but echoes keystrokes immediately', () => {
    vi.useFakeTimers();
    const { result } = renderUrlState();

    act(() => {
      result.current.table.onColumnFiltersChange([{ id: 'name', value: 'al' }]);
    });

    // The input sees the value at once; the URL only after the quiet period.
    expect(result.current.table.columnFilters).toEqual([{ id: 'name', value: 'al' }]);
    expect(result.current.searchParams.get('people_f_name')).toBeNull();

    act(() => {
      vi.advanceTimersByTime(300);
    });
    expect(result.current.searchParams.get('people_f_name')).toBe('al');
    vi.useRealTimers();
  });

  it('preserves unrelated params and clears only its own', () => {
    const { result } = renderUrlState('/?tab=members&edit=abc&people_f_status=active');

    act(() => {
      result.current.table.onColumnFiltersChange([]);
    });

    expect(result.current.searchParams.get('tab')).toBe('members');
    expect(result.current.searchParams.get('edit')).toBe('abc');
    expect(result.current.searchParams.get('people_f_status')).toBeNull();
  });

  it('isolates two tables on one page by prefix', () => {
    const { result } = renderHook(
      () => {
        const users = useTableUrlState('users', columns);
        const invites = useTableUrlState('invites', columns);
        const [searchParams] = useSearchParams();
        return { users, invites, searchParams };
      },
      { wrapper: wrapperWith('/') },
    );

    act(() => {
      result.current.users.onColumnFiltersChange([{ id: 'status', value: ['active'] }]);
    });

    expect(result.current.searchParams.get('users_f_status')).toBe('active');
    expect(result.current.invites.columnFilters).toEqual([]);
  });

  it('ignores invalid params silently', () => {
    const { result } = renderUrlState(
      // Unknown column, bad direction, filter on a filterless column, malformed range/number.
      '/?people_s=name.sideways&people_f_ghost=x&people_f_actions=x&people_f_size=notarange&people_f_createdAt=2026-01-01',
    );

    expect(result.current.table.sorting).toEqual([]);
    expect(result.current.table.columnFilters).toEqual([]);
  });

  it('a decoded text filter containing dots is not misread as a range', () => {
    const { result } = renderUrlState('/?people_f_name=v2..3-beta');

    // Meta-driven decoding: name is text, so the value passes through verbatim.
    expect(result.current.table.columnFilters).toEqual([{ id: 'name', value: 'v2..3-beta' }]);
  });
});
