import { render, act } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';

// Capture the handlers DropColumnIndicator registers with dnd-kit so we can drive
// drag events directly — no DndContext / real drag needed.
let handlers: Record<string, (e: unknown) => void> = {};
vi.mock('@dnd-kit/core', () => ({
  useDndMonitor: (h: Record<string, (e: unknown) => void>) => { handlers = h; },
}));

import { DropColumnIndicator } from './DropColumnIndicator';

const HIGHLIGHT = '[class*="border-blue-500"]';

// 4 columns of 100px across a track at left=100 (so x∈[100,500]).
function dragOver(pointerX: number) {
  return {
    over: {
      data: { current: { type: 'space-track', columnStartsMs: [0, 1, 2, 3] } },
      rect: { left: 100, width: 400, height: 50, top: 20 },
    },
    activatorEvent: { clientX: pointerX } as PointerEvent,
    delta: { x: 0 },
  };
}

describe('DropColumnIndicator', () => {
  beforeEach(() => { handlers = {}; });

  it('highlights the hovered column on drag-over and clears on drag-end', () => {
    render(<DropColumnIndicator />);
    expect(document.querySelector(HIGHLIGHT)).toBeNull();

    // pointerX 150 → 0.125 across the track → column 0 → left = 100, width = 100.
    act(() => handlers.onDragMove(dragOver(150)));
    const hl = document.querySelector(HIGHLIGHT) as HTMLElement | null;
    expect(hl).not.toBeNull();
    expect(hl!.style.left).toBe('100px');
    expect(hl!.style.width).toBe('100px');

    // pointerX 450 → 0.875 across → last column (idx 3) → left = 400.
    act(() => handlers.onDragMove(dragOver(450)));
    expect((document.querySelector(HIGHLIGHT) as HTMLElement).style.left).toBe('400px');

    act(() => handlers.onDragEnd(undefined));
    expect(document.querySelector(HIGHLIGHT)).toBeNull();
  });

  it('shows nothing when the pointer is over a non-track droppable', () => {
    render(<DropColumnIndicator />);
    act(() => handlers.onDragMove(dragOver(150)));
    expect(document.querySelector(HIGHLIGHT)).not.toBeNull();

    act(() => handlers.onDragOver({ over: { data: { current: { type: 'other' } } } }));
    expect(document.querySelector(HIGHLIGHT)).toBeNull();
  });
});
