import { render, act } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';

// Capture the handlers DropPositionIndicator registers with dnd-kit so we can drive
// drag events directly — no DndContext / real drag needed.
let handlers: Record<string, (e: unknown) => void> = {};
vi.mock('@dnd-kit/core', () => ({
  useDndMonitor: (h: Record<string, (e: unknown) => void>) => { handlers = h; },
}));

import { DropPositionIndicator } from './DropPositionIndicator';

const HIGHLIGHT = '[class*="bg-blue-500"]';

// A 400px track at left=100 spanning a 4000ms window in 4 columns of 1000ms each,
// so 1ms = 0.1px. The dragged bar occupies the second column (1000 → 2000ms).
function dragBy(deltaX: number) {
  return {
    over: {
      data: { current: { type: 'space-track', viewStartMs: 0, viewEndMs: 4000 } },
      rect: { left: 100, width: 400, height: 50, top: 20 },
    },
    active: { data: { current: { startTs: new Date(1000).toISOString(), endTs: new Date(2000).toISOString() } } },
    delta: { x: deltaX },
  };
}

describe('DropPositionIndicator', () => {
  beforeEach(() => { handlers = {}; });

  it('tracks the landing position continuously and clears on drag-end', () => {
    render(<DropPositionIndicator scale="week" />);
    expect(document.querySelector(HIGHLIGHT)).toBeNull();

    // No movement → the marker sits on the bar's own start (1000ms → 25% → x=200).
    act(() => handlers.onDragMove(dragBy(0)));
    const hl = document.querySelector(HIGHLIGHT) as HTMLElement | null;
    expect(hl).not.toBeNull();
    expect(hl!.style.left).toBe('200px');
    // A slim marker, NOT a full-column fill (100px): a box painted over the whole
    // day competed with the dragged bar and made the drag look quantized.
    expect(hl!.style.width).toBe('3px');
    expect(hl!.style.height).toBe('50px');

    // 10px right = 100ms — a fraction of a column. The marker must follow it rather
    // than stay pinned to the column edge; this is the snap the grid used to have.
    act(() => handlers.onDragMove(dragBy(10)));
    expect((document.querySelector(HIGHLIGHT) as HTMLElement).style.left).toBe('210px');

    act(() => handlers.onDragMove(dragBy(55)));
    expect((document.querySelector(HIGHLIGHT) as HTMLElement).style.left).toBe('255px');

    act(() => handlers.onDragEnd(undefined));
    expect(document.querySelector(HIGHLIGHT)).toBeNull();
  });

  it('labels the marker with the landing instant, at the scale\'s precision', () => {
    // The view is 1970-01-01T00:00Z → 04:00Z here; a +10px drag lands the bar at
    // 1000ms + 100ms. Rendered in the browser's local zone, so assert on the parts
    // the scale governs rather than a fixed clock reading.
    const { rerender } = render(<DropPositionIndicator scale="week" />);
    act(() => handlers.onDragMove(dragBy(10)));
    // week → day columns, so the day is named AND the time, which is what the drag aims at.
    expect(document.body.textContent).toMatch(/Jan.*\d{2}:\d{2}/);

    // hour → the day is a given; only the clock is in play.
    rerender(<DropPositionIndicator scale="hour" />);
    act(() => handlers.onDragMove(dragBy(10)));
    expect(document.body.textContent).toMatch(/^\d{2}:\d{2}$/);

    // month → week columns: one pixel spans hours, so naming a minute would be
    // false precision. The day is the unit the user can actually aim for.
    rerender(<DropPositionIndicator scale="month" />);
    act(() => handlers.onDragMove(dragBy(10)));
    expect(document.body.textContent).toMatch(/Jan/);
    expect(document.body.textContent).not.toMatch(/\d{2}:\d{2}/);
  });

  it('holds the marker inside the track when the drag overshoots its end', () => {
    render(<DropPositionIndicator scale="week" />);
    // Far past the right edge: the bar's start clamps to viewEnd - duration
    // (3000ms → 75% → x=400), keeping the whole bar visible.
    act(() => handlers.onDragMove(dragBy(9999)));
    expect((document.querySelector(HIGHLIGHT) as HTMLElement).style.left).toBe('400px');
  });

  it('draws no marker for a bar that starts before the window', () => {
    render(<DropPositionIndicator scale="week" />);
    // Such a bar keeps its own start (clamping it would move a request the user
    // only picked up), and that start has no honest position on this track.
    act(() => handlers.onDragMove({
      ...dragBy(0),
      active: { data: { current: { startTs: new Date(-5000).toISOString(), endTs: new Date(-4000).toISOString() } } },
    }));
    expect(document.querySelector(HIGHLIGHT)).toBeNull();
  });

  it('shows nothing when the pointer is over a non-track droppable', () => {
    render(<DropPositionIndicator scale="week" />);
    act(() => handlers.onDragMove(dragBy(0)));
    expect(document.querySelector(HIGHLIGHT)).not.toBeNull();

    act(() => handlers.onDragOver({ over: { data: { current: { type: 'other' } } }, active: { data: { current: {} } } }));
    expect(document.querySelector(HIGHLIGHT)).toBeNull();
  });
});
