/** @jsxImportSource react */
import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import { DrawingPreviewSvg } from './DrawingPreviewSvg';
import type { Coordinate, DrawingMode } from '@foundation/src/types/geometry';

function renderPreview(
  drawingMode: DrawingMode,
  drawingPoints: Coordinate[],
  mousePosition: Coordinate | null,
) {
  const { container } = render(
    <svg>
      <DrawingPreviewSvg
        drawingMode={drawingMode}
        drawingPoints={drawingPoints}
        mousePosition={mousePosition}
      />
    </svg>,
  );
  return container;
}

describe('DrawingPreviewSvg', () => {
  describe('circle', () => {
    it('shows nothing before the centre is placed', () => {
      const container = renderPreview('circle', [], { x: 50, y: 50 });
      expect(container.querySelectorAll('circle')).toHaveLength(0);
    });

    it('grows from the centre as the pointer moves', () => {
      const container = renderPreview('circle', [{ x: 100, y: 100 }], { x: 100, y: 160 });
      // The outline plus the centre dot.
      const circles = container.querySelectorAll('circle');

      expect(circles).toHaveLength(2);
      expect(circles[0]).toHaveAttribute('cx', '100');
      expect(circles[0]).toHaveAttribute('r', '60');
    });

    it('draws the radius line, so it is clear which point is anchored', () => {
      const container = renderPreview('circle', [{ x: 100, y: 100 }], { x: 140, y: 130 });
      const line = container.querySelector('line');

      expect(line).toHaveAttribute('x1', '100');
      expect(line).toHaveAttribute('x2', '140');
    });

    it('shows nothing once the pointer has left the canvas', () => {
      const container = renderPreview('circle', [{ x: 100, y: 100 }], null);
      expect(container.querySelectorAll('circle')).toHaveLength(0);
    });
  });
});
