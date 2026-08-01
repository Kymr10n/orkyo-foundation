/** @jsxImportSource react */
import { StrictMode } from 'react';
import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter, useSearchParams } from 'react-router';
import { useEditQueryParam } from './useEditQueryParam';

interface Item {
  id: string;
  name: string;
}

// Renders the hook and surfaces the live query string so param-clearing is observable.
function Harness({
  items,
  onOpen,
  ready,
  getId,
}: {
  items?: readonly Item[];
  onOpen: (item: Item) => void;
  ready?: boolean;
  getId?: (item: Item) => string;
}) {
  const [searchParams] = useSearchParams();
  useEditQueryParam(items, onOpen, { ready, getId });
  return <div data-testid="qs">{searchParams.toString()}</div>;
}

function renderAt(url: string, props: Omit<Parameters<typeof Harness>[0], never>) {
  return render(
    <MemoryRouter initialEntries={[url]}>
      <Harness {...props} />
    </MemoryRouter>,
  );
}

const items: Item[] = [
  { id: 'a', name: 'Alpha' },
  { id: 'b', name: 'Beta' },
];

describe('useEditQueryParam', () => {
  it('opens the matching item when ?edit=<id> is present', () => {
    const onOpen = vi.fn();
    renderAt('/?edit=b', { items, onOpen });
    expect(onOpen).toHaveBeenCalledTimes(1);
    expect(onOpen).toHaveBeenCalledWith(items[1]);
  });

  it('does nothing when there is no edit param', () => {
    const onOpen = vi.fn();
    renderAt('/', { items, onOpen });
    expect(onOpen).not.toHaveBeenCalled();
  });

  it('does nothing when the id matches no item', () => {
    const onOpen = vi.fn();
    renderAt('/?edit=zzz', { items, onOpen });
    expect(onOpen).not.toHaveBeenCalled();
  });

  it('waits until ready before opening', () => {
    const onOpen = vi.fn();
    const { rerender } = renderAt('/?edit=a', { items, onOpen, ready: false });
    expect(onOpen).not.toHaveBeenCalled();

    rerender(
      <MemoryRouter initialEntries={['/?edit=a']}>
        <Harness items={items} onOpen={onOpen} ready={true} />
      </MemoryRouter>,
    );
    expect(onOpen).toHaveBeenCalledWith(items[0]);
  });

  it('does nothing while the list is still empty', () => {
    const onOpen = vi.fn();
    renderAt('/?edit=a', { items: [], onOpen });
    expect(onOpen).not.toHaveBeenCalled();
  });

  it('clears only the edit param and preserves the rest', () => {
    const onOpen = vi.fn();
    renderAt('/?edit=a&tab=teams', { items, onOpen });
    const qs = screen.getByTestId('qs').textContent ?? '';
    expect(qs).toContain('tab=teams');
    expect(qs).not.toContain('edit');
  });

  it('opens only once across rerenders', () => {
    const onOpen = vi.fn();
    const { rerender } = renderAt('/?edit=a', { items, onOpen });
    rerender(
      <MemoryRouter initialEntries={['/?edit=a']}>
        <Harness items={items} onOpen={onOpen} />
      </MemoryRouter>,
    );
    expect(onOpen).toHaveBeenCalledTimes(1);
  });

  it('opens only once under StrictMode double-invocation', () => {
    // StrictMode re-runs the mount effect before the param-clear commits; the
    // hook must still fire onOpen exactly once for the id.
    const onOpen = vi.fn();
    render(
      <StrictMode>
        <MemoryRouter initialEntries={['/?edit=a']}>
          <Harness items={items} onOpen={onOpen} />
        </MemoryRouter>
      </StrictMode>,
    );
    expect(onOpen).toHaveBeenCalledTimes(1);
  });

  it('honours a custom getId', () => {
    const onOpen = vi.fn();
    const byName: Item[] = [{ id: 'x', name: 'Alpha' }];
    renderAt('/?edit=Alpha', { items: byName, onOpen, getId: (i) => i.name });
    expect(onOpen).toHaveBeenCalledWith(byName[0]);
  });
});
