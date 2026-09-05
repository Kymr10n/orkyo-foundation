import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { RequestTimelineRow } from './RequestTimelineRow';
import type { TimeColumn } from './scheduler-types';
import type { Conflict } from '@foundation/src/types/requests';
import { makeRequest } from '@foundation/src/test-utils/request-fixtures';

// An 8-hour window, 08:00–16:00, in eight one-hour columns.
const columns: TimeColumn[] = Array.from({ length: 8 }, (_, i) => {
  const start = new Date(Date.UTC(2026, 2, 3, 8 + i));
  return { start, end: new Date(start.getTime() + 3_600_000), label: `${8 + i}:00` };
});

const request = makeRequest({
  id: 'r1',
  name: 'Fabricate frame',
  status: 'in_progress',
  startTs: '2026-03-03T09:00:00Z',
  endTs: '2026-03-03T11:00:00Z',
});

const twoConflicts: Conflict[] = [
  { id: 'c1', kind: 'overlap', severity: 'error', message: 'Overlaps Finish weld' },
  { id: 'c2', kind: 'starts_in_off_time', severity: 'warning', message: 'Starts in off-time' },
];

function renderRow(props: Partial<React.ComponentProps<typeof RequestTimelineRow>> = {}) {
  const onRequestClick = vi.fn();
  const onRequestDoubleClick = vi.fn();
  const result = render(
    <RequestTimelineRow
      request={request}
      columns={columns}
      columnMinWidthPx={60}
      onRequestClick={onRequestClick}
      onRequestDoubleClick={onRequestDoubleClick}
      {...props}
    />,
  );
  return { ...result, onRequestClick, onRequestDoubleClick };
}

describe('RequestTimelineRow', () => {
  it('labels the row with the request name and its status', () => {
    renderRow();
    expect(screen.getByTestId('request-row-r1')).toBeInTheDocument();
    // Name appears in the label cell and again on the bar.
    expect(screen.getAllByText('Fabricate frame').length).toBeGreaterThanOrEqual(2);
    expect(screen.getByText('In Progress')).toBeInTheDocument();
  });

  it('places the bar by its share of the visible window', () => {
    renderRow();
    const bar = screen.getByTestId('request-canvas-bar');
    // 09–11 in an 08–16 window: starts one eighth in, spans two eighths.
    expect(bar.style.left).toBe('12.5%');
    expect(bar.style.width).toBe('25%');
  });

  it('clips a bar that starts before the window to its left edge', () => {
    renderRow({
      request: makeRequest({ ...request, startTs: '2026-03-03T06:00:00Z', endTs: '2026-03-03T10:00:00Z' }),
    });
    const bar = screen.getByTestId('request-canvas-bar');
    expect(bar.style.left).toBe('0%');
    expect(bar.style.width).toBe('25%');
  });

  it('draws no bar for a request without dates', () => {
    renderRow({ request: makeRequest({ id: 'r1', name: 'Undated', startTs: null, endTs: null }) });
    expect(screen.queryByTestId('request-canvas-bar')).not.toBeInTheDocument();
    expect(screen.getByTestId('request-row-r1')).toBeInTheDocument();
  });

  it('reads as Assigned when nothing is wrong', () => {
    renderRow();
    const bar = screen.getByTestId('request-canvas-bar');
    expect(bar).toHaveAttribute('aria-label', 'Fabricate frame, Assigned. Open request.');
    expect(bar).toHaveAttribute('title', 'Fabricate frame');
    expect(bar.className).toContain('bg-blue-100');
    expect(bar.className).not.toContain('bg-red-100');
    expect(bar.querySelector('svg')).toBeNull();
  });

  it('reads as Overbooked with the conflict count when the registry flags it', () => {
    renderRow({ conflicts: twoConflicts });
    const bar = screen.getByTestId('request-canvas-bar');
    expect(bar).toHaveAttribute('aria-label', 'Fabricate frame, Overbooked, 2 conflicts. Open request.');
    expect(bar).toHaveAttribute('title', 'Fabricate frame (2 conflicts)');
    expect(bar.className).toContain('bg-red-100');
    // The conflict marker icon, so the state does not rely on colour alone.
    expect(bar.querySelector('svg')).not.toBeNull();
  });

  it('uses the singular for one conflict', () => {
    renderRow({ conflicts: [twoConflicts[0]] });
    expect(screen.getByTestId('request-canvas-bar')).toHaveAttribute(
      'aria-label',
      'Fabricate frame, Overbooked, 1 conflict. Open request.',
    );
  });

  it('reports a click with its position and leaves double-click alone', () => {
    const { onRequestClick, onRequestDoubleClick } = renderRow();
    fireEvent.click(screen.getByTestId('request-canvas-bar'), { clientX: 40, clientY: 12 });
    expect(onRequestClick).toHaveBeenCalledTimes(1);
    expect(onRequestClick).toHaveBeenCalledWith('r1', { x: 40, y: 12 });
    expect(onRequestDoubleClick).not.toHaveBeenCalled();
  });

  it('reports a double-click', () => {
    const { onRequestDoubleClick } = renderRow();
    fireEvent.doubleClick(screen.getByTestId('request-canvas-bar'));
    expect(onRequestDoubleClick).toHaveBeenCalledWith('r1');
  });

  it('opens the editor from the keyboard with Enter or Space, and ignores other keys', () => {
    const { onRequestClick, onRequestDoubleClick } = renderRow();
    const bar = screen.getByTestId('request-canvas-bar');
    expect(bar).toHaveAttribute('role', 'button');
    expect(bar).toHaveAttribute('tabindex', '0');

    fireEvent.keyDown(bar, { key: 'Enter' });
    fireEvent.keyDown(bar, { key: ' ' });
    fireEvent.keyDown(bar, { key: 'Escape' });

    expect(onRequestDoubleClick).toHaveBeenCalledTimes(2);
    expect(onRequestDoubleClick).toHaveBeenCalledWith('r1');
    expect(onRequestClick).not.toHaveBeenCalled();
  });

  it('ignores key presses that bubble up from inside the bar', () => {
    const { onRequestDoubleClick } = renderRow();
    const inner = screen.getByTestId('request-canvas-bar').querySelector('div') as HTMLElement;
    fireEvent.keyDown(inner, { key: 'Enter' });
    expect(onRequestDoubleClick).not.toHaveBeenCalled();
  });

  it('survives clicks with no handlers wired', () => {
    render(<RequestTimelineRow request={request} columns={columns} columnMinWidthPx={60} />);
    const bar = screen.getByTestId('request-canvas-bar');
    expect(() => {
      fireEvent.click(bar);
      fireEvent.doubleClick(bar);
      fireEvent.keyDown(bar, { key: 'Enter' });
    }).not.toThrow();
  });

  it('passes the zoomed column width to every cell', () => {
    const { container } = renderRow({ columnMinWidthPx: 90 });
    const cells = container.querySelectorAll<HTMLElement>('[data-column-cell]');
    expect(cells).toHaveLength(columns.length);
    for (const cell of cells) expect(cell.style.minWidth).toBe('90px');
  });
});
