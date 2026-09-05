import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RequestCanvas, ZOOM_MAX, ZOOM_STEP } from './RequestCanvas';
import { useAppStore } from '@foundation/src/store/app-store';
import { makeRequest } from '@foundation/src/test-utils/request-fixtures';
import type { Conflict, Request } from '@foundation/src/types/requests';

vi.mock('@foundation/src/lib/api/request-api', () => ({
  getRequests: vi.fn(),
}));

import { getRequests } from '@foundation/src/lib/api/request-api';

// Week scale from a Monday: seven day columns, Mon 2 Mar – Sun 8 Mar 2026 (local time).
const ANCHOR = new Date(2026, 2, 2, 0, 0, 0);
const inWindow = (day: number, hour: number) => new Date(2026, 2, day, hour).toISOString();

const parent = makeRequest({ id: 'p1', name: 'Assembly', planningMode: 'summary', sortOrder: 1 });
const childA = makeRequest({
  id: 'a', name: 'Fabricate frame', parentRequestId: 'p1',
  startTs: inWindow(3, 8), endTs: inWindow(3, 12), isScheduled: true,
});
const childB = makeRequest({
  id: 'b', name: 'Finish weld', parentRequestId: 'p1',
  startTs: inWindow(4, 8), endTs: inWindow(4, 12), isScheduled: true,
});
const loose = makeRequest({
  id: 'l', name: 'Inspect', startTs: inWindow(5, 8), endTs: inWindow(5, 12), isScheduled: true,
});
const farAway = makeRequest({
  id: 'far', name: 'Next month', startTs: new Date(2026, 3, 20, 8).toISOString(),
  endTs: new Date(2026, 3, 20, 12).toISOString(), isScheduled: true,
});

function renderCanvas(props: Partial<React.ComponentProps<typeof RequestCanvas>> = {}) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const onRequestClick = vi.fn();
  const onRequestDoubleClick = vi.fn();
  const result = render(
    <QueryClientProvider client={queryClient}>
      <RequestCanvas
        requests={[childA, childB, loose, farAway]}
        lookup={[childA, childB, loose, farAway, parent]}
        conflicts={new Map()}
        scale="week"
        anchorTs={ANCHOR}
        nowMs={new Date(2026, 2, 4, 10).getTime()}
        siteId="site-1"
        onRequestClick={onRequestClick}
        onRequestDoubleClick={onRequestDoubleClick}
        {...props}
      />
    </QueryClientProvider>,
  );
  return { ...result, onRequestClick, onRequestDoubleClick };
}

function cellWidths(container: HTMLElement): string[] {
  return Array.from(container.querySelectorAll<HTMLElement>('[data-column-cell]')).map((c) => c.style.minWidth);
}

/**
 * Dispatches a wheel event with modifier keys. happy-dom's WheelEvent leaves ctrlKey/metaKey
 * undefined whatever the init says, so they are pinned onto the instance. Returns false when the
 * listener called preventDefault, like dispatchEvent does.
 */
function wheel(el: HTMLElement, deltaY: number, mods: { ctrlKey?: boolean; metaKey?: boolean } = {}): boolean {
  const event = new WheelEvent('wheel', { deltaY, bubbles: true, cancelable: true });
  Object.defineProperty(event, 'ctrlKey', { value: mods.ctrlKey ?? false });
  Object.defineProperty(event, 'metaKey', { value: mods.metaKey ?? false });
  return el.dispatchEvent(event);
}

describe('RequestCanvas', () => {
  beforeEach(() => {
    useAppStore.setState({ collapsedGroupIds: [] });
    vi.mocked(getRequests).mockReset();
    vi.mocked(getRequests).mockResolvedValue([]);
  });

  it('draws a row for every request in the window and none for one outside it', () => {
    renderCanvas();
    expect(screen.getByText('Task')).toBeInTheDocument();
    expect(screen.getByTestId('request-row-a')).toBeInTheDocument();
    expect(screen.getByTestId('request-row-b')).toBeInTheDocument();
    expect(screen.getByTestId('request-row-l')).toBeInTheDocument();
    expect(screen.queryByTestId('request-row-far')).not.toBeInTheDocument();
    expect(screen.getAllByTestId('request-canvas-bar')).toHaveLength(3);
  });

  it('heads each group with the parent from the page feeds, and the rest with Ungrouped', () => {
    renderCanvas();
    expect(screen.getByText('Assembly')).toBeInTheDocument();
    expect(screen.getByText('Ungrouped')).toBeInTheDocument();
    expect(getRequests).toHaveBeenCalledWith(true, 'site-1');
  });

  it('names a parent the page does not hold from the request list', async () => {
    vi.mocked(getRequests).mockResolvedValue([parent]);
    renderCanvas({ lookup: [childA, childB, loose] });
    expect(await screen.findByText('Assembly')).toBeInTheDocument();
    expect(screen.queryByText('Unknown group')).not.toBeInTheDocument();
  });

  it('falls back to "Unknown group" when nobody knows the parent', () => {
    renderCanvas({ lookup: [childA, childB, loose] });
    expect(screen.getByText('Unknown group')).toBeInTheDocument();
  });

  it('looks parents up tenant-wide when no site is selected, and shades hours outside the working day', () => {
    // Day scale: 24 hour columns from the anchor's hour, so the 08–17 working day leaves the
    // early and late columns shaded.
    const { container } = renderCanvas({
      siteId: null,
      scale: 'day',
      anchorTs: new Date(2026, 2, 3, 0),
      workingHoursEnabled: true,
      workingDayStart: '08:00',
      workingDayEnd: '17:00',
    });
    expect(getRequests).toHaveBeenCalledWith(true, undefined);
    const shaded = container.querySelectorAll('[data-column-cell][title="Outside working hours"]');
    expect(shaded.length).toBeGreaterThan(0);
    expect(screen.getByTestId('request-row-a')).toBeInTheDocument();
  });

  it('collapses a group from its header', () => {
    renderCanvas();
    fireEvent.click(screen.getByText('Assembly'));
    expect(screen.queryByTestId('request-row-a')).not.toBeInTheDocument();
    expect(screen.queryByTestId('request-row-b')).not.toBeInTheDocument();
    expect(screen.getByTestId('request-row-l')).toBeInTheDocument();
    expect(useAppStore.getState().collapsedGroupIds).toContain('requests:p1');
  });

  it('shows the empty message when nothing is scheduled in the window', () => {
    renderCanvas({ requests: [farAway] });
    expect(screen.getByText('No scheduled tasks in this period.')).toBeInTheDocument();
  });

  it('shows the loading state instead of rows', () => {
    renderCanvas({ isLoading: true });
    expect(screen.getByText('Loading…')).toBeInTheDocument();
    expect(screen.queryByTestId('request-row-a')).not.toBeInTheDocument();
  });

  it('marks now when it falls inside the window', () => {
    renderCanvas();
    expect(screen.getByTestId('now-line')).toBeInTheDocument();
  });

  it('hides the now marker when now is outside the window', () => {
    renderCanvas({ nowMs: new Date(2026, 5, 1).getTime() });
    expect(screen.queryByTestId('now-line')).not.toBeInTheDocument();
  });

  it('colours a bar from the conflict registry', () => {
    const conflicts = new Map<string, Conflict[]>([
      ['a', [{ id: 'c1', kind: 'overlap', severity: 'error', message: 'Overlaps' }]],
    ]);
    renderCanvas({ conflicts });
    expect(screen.getByLabelText('Fabricate frame, Overbooked, 1 conflict. Open request.')).toBeInTheDocument();
    expect(screen.getByLabelText('Finish weld, Assigned. Open request.')).toBeInTheDocument();
  });

  it('passes bar clicks and double-clicks up', () => {
    const { onRequestClick, onRequestDoubleClick } = renderCanvas();
    const bar = screen.getByLabelText('Inspect, Assigned. Open request.');
    fireEvent.click(bar, { clientX: 5, clientY: 6 });
    fireEvent.doubleClick(bar);
    expect(onRequestClick).toHaveBeenCalledWith('l', { x: 5, y: 6 });
    expect(onRequestDoubleClick).toHaveBeenCalledWith('l');
  });

  it('renders the legend and filter bar slots in its toolbar', () => {
    renderCanvas({
      legend: <span data-testid="legend-slot">key</span>,
      filterBar: <span data-testid="filter-slot">filters</span>,
    });
    expect(screen.getByTestId('legend-slot')).toBeInTheDocument();
    expect(screen.getByTestId('filter-slot')).toBeInTheDocument();
  });

  describe('zoom', () => {
    it('starts at fit-to-width with only Zoom in enabled', () => {
      const { container } = renderCanvas();
      expect(new Set(cellWidths(container))).toEqual(new Set(['60px']));
      expect(screen.getByRole('button', { name: 'Zoom out' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Reset zoom' })).toBeDisabled();
      expect(screen.getByRole('button', { name: 'Zoom in' })).toBeEnabled();
    });

    it('widens every column per step and stops at the maximum', () => {
      const { container } = renderCanvas();
      const zoomIn = screen.getByRole('button', { name: 'Zoom in' });

      fireEvent.click(zoomIn);
      expect(new Set(cellWidths(container))).toEqual(new Set(['90px']));
      expect(screen.getByRole('button', { name: 'Zoom out' })).toBeEnabled();
      expect(screen.getByRole('button', { name: 'Reset zoom' })).toBeEnabled();

      const stepsToMax = Math.round((ZOOM_MAX - 1) / ZOOM_STEP);
      for (let i = 1; i < stepsToMax + 3; i++) fireEvent.click(zoomIn);
      expect(new Set(cellWidths(container))).toEqual(new Set(['240px']));
      expect(zoomIn).toBeDisabled();
    });

    it('steps back out and resets to fit', () => {
      const { container } = renderCanvas();
      const zoomIn = screen.getByRole('button', { name: 'Zoom in' });
      fireEvent.click(zoomIn);
      fireEvent.click(zoomIn);
      expect(new Set(cellWidths(container))).toEqual(new Set(['120px']));

      fireEvent.click(screen.getByRole('button', { name: 'Zoom out' }));
      expect(new Set(cellWidths(container))).toEqual(new Set(['90px']));

      fireEvent.click(screen.getByRole('button', { name: 'Reset zoom' }));
      expect(new Set(cellWidths(container))).toEqual(new Set(['60px']));
    });

    it('keeps the virtual rows as wide as the zoomed columns', () => {
      renderCanvas();
      fireEvent.click(screen.getByRole('button', { name: 'Zoom in' }));
      const wrapper = screen.getByTestId('request-row-a').parentElement as HTMLElement;
      expect(wrapper.style.minWidth).toBe(`${208 + 7 * 90}px`);
    });

    it('zooms on Ctrl+wheel and ⌘+wheel, claiming the event from the browser', () => {
      const { container } = renderCanvas();
      const canvas = screen.getByTestId('request-canvas');

      let notPrevented = true;
      act(() => { notPrevented = wheel(canvas, -100, { ctrlKey: true }); });
      expect(notPrevented).toBe(false); // preventDefault called
      expect(new Set(cellWidths(container))).toEqual(new Set(['90px']));

      act(() => { notPrevented = wheel(canvas, -100, { metaKey: true }); });
      expect(notPrevented).toBe(false);
      expect(new Set(cellWidths(container))).toEqual(new Set(['120px']));

      act(() => { wheel(canvas, 100, { ctrlKey: true }); });
      expect(new Set(cellWidths(container))).toEqual(new Set(['90px']));
    });

    it('leaves a plain wheel to scroll the grid', () => {
      const { container } = renderCanvas();
      let notPrevented = false;
      act(() => { notPrevented = wheel(screen.getByTestId('request-canvas'), -100); });
      expect(notPrevented).toBe(true);
      expect(new Set(cellWidths(container))).toEqual(new Set(['60px']));
    });

    it('does not wheel past the maximum', () => {
      const { container } = renderCanvas();
      const canvas = screen.getByTestId('request-canvas');
      act(() => {
        for (let i = 0; i < 12; i++) wheel(canvas, -100, { ctrlKey: true });
      });
      expect(new Set(cellWidths(container))).toEqual(new Set(['240px']));
    });
  });
});

// Keep the fixture type honest: a Request literal that drifts from the type fails here first.
const _typeCheck: Request = childA;
void _typeCheck;
