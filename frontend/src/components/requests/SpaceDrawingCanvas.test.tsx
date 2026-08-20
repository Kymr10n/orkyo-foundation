/** @jsxImportSource react */
import { describe, expect, it, vi, beforeEach } from 'vitest';
import { fireEvent, render } from '@testing-library/react';
import { SpaceDrawingCanvas } from './SpaceDrawingCanvas';
import type { ResourceGeometry } from '@foundation/src/types/geometry';

const geometry: ResourceGeometry = {
  type: 'rectangle',
  coordinates: [
    { x: 10, y: 10 },
    { x: 50, y: 50 },
  ],
};

const space = { id: 'space-1', name: 'Office A', code: 'A-01', geometry };

const onSpaceMove = vi.fn();
const onSpaceClick = vi.fn();

function renderCanvas(props: Partial<React.ComponentProps<typeof SpaceDrawingCanvas>> = {}) {
  return render(
    <SpaceDrawingCanvas
      drawingMode="none"
      onDrawingComplete={vi.fn()}
      existingSpaces={[space]}
      editEnabled
      onSpaceMove={onSpaceMove}
      onSpaceClick={onSpaceClick}
      onDrawingCancel={vi.fn()}
      {...props}
    />,
  );
}

/** The shape's own hit area, which carries the id the handlers read. */
function shape() {
  return document.querySelector('[data-space-id="space-1"]')!;
}

describe('SpaceDrawingCanvas — press, hold, release', () => {
  beforeEach(() => vi.clearAllMocks());

  it('selects on press, so the resize handles are there to grab', () => {
    renderCanvas({ selectedResourceId: undefined });

    fireEvent.mouseDown(shape(), { clientX: 100, clientY: 100 });

    expect(onSpaceClick).toHaveBeenCalledWith('space-1');
  });

  it('does not move the shape when a click wobbles', () => {
    // The bug this pins: pressing armed the drag outright, and a hand never holds still between
    // press and release — so selecting a shape crept it a pixel every time.
    const { container } = renderCanvas();
    const canvas = container.querySelector('[data-testid="drawing-canvas-surface"]') ?? shape();

    fireEvent.mouseDown(shape(), { clientX: 100, clientY: 100 });
    fireEvent.mouseMove(canvas, { clientX: 102, clientY: 101 });
    fireEvent.mouseUp(canvas, { clientX: 102, clientY: 101 });

    expect(onSpaceMove).not.toHaveBeenCalled();
  });

  it('moves the shape once the pointer travels far enough to mean it', () => {
    const { container } = renderCanvas();
    const canvas = container.querySelector('[data-testid="drawing-canvas-surface"]') ?? shape();

    fireEvent.mouseDown(shape(), { clientX: 100, clientY: 100 });
    fireEvent.mouseMove(canvas, { clientX: 140, clientY: 130 });
    fireEvent.mouseUp(canvas, { clientX: 140, clientY: 130 });

    expect(onSpaceMove).toHaveBeenCalledTimes(1);
    const [id, moved] = onSpaceMove.mock.calls[0];
    expect(id).toBe('space-1');
    // Every corner shifts by the same delta — a move, not a reshape.
    expect(moved.coordinates[0]).toEqual({ x: 50, y: 40 });
    expect(moved.coordinates[1]).toEqual({ x: 90, y: 80 });
  });

  it('does not move anything while editing is off', () => {
    const { container } = renderCanvas({ editEnabled: false });
    const canvas = container.firstElementChild!;

    fireEvent.mouseDown(shape(), { clientX: 100, clientY: 100 });
    fireEvent.mouseMove(canvas, { clientX: 140, clientY: 130 });
    fireEvent.mouseUp(canvas, { clientX: 140, clientY: 130 });

    expect(onSpaceMove).not.toHaveBeenCalled();
  });
});
