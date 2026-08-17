/** @jsxImportSource react */
import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import { SpaceShapeSvg } from './SpaceShapeSvg';
import type { ResourceGeometry } from '@foundation/src/types/geometry';

function renderShape(geometry: ResourceGeometry | undefined, props = {}) {
  const { container } = render(
    <svg>
      <SpaceShapeSvg
        space={{ id: 'station-1', name: 'Bay 3', code: 'B-3', geometry }}
        {...props}
      />
    </svg>,
  );
  return container;
}

const circle: ResourceGeometry = {
  type: 'circle',
  coordinates: [
    { x: 200, y: 200 },  // centre
    { x: 230, y: 240 },  // rim: dx 30, dy 40 -> r 50
  ],
};

describe('SpaceShapeSvg', () => {
  it('renders nothing for a shape type it does not know', () => {
    // The component falls through to null rather than throwing, which is what made a missing
    // branch invisible: the station simply never appeared on the plan.
    const container = renderShape({
      type: 'hexagon' as ResourceGeometry['type'],
      coordinates: [{ x: 0, y: 0 }, { x: 10, y: 10 }],
    });

    expect(container.querySelector('[data-space-id]')).toBeNull();
  });

  it('renders nothing when the resource has no shape at all', () => {
    expect(renderShape(undefined).querySelector('[data-space-id]')).toBeNull();
  });

  describe('circle', () => {
    it('derives the radius from the gap between centre and rim', () => {
      const container = renderShape(circle);
      const shape = container.querySelector('circle');

      expect(shape).toHaveAttribute('cx', '200');
      expect(shape).toHaveAttribute('cy', '200');
      expect(shape).toHaveAttribute('r', '50');
    });

    it('labels the circle at its centre', () => {
      const label = renderShape(circle).querySelector('text');

      expect(label).toHaveTextContent('B-3');
      expect(label).toHaveAttribute('x', '200');
      expect(label).toHaveAttribute('y', '200');
    });

    it('offers exactly one handle, on the rim', () => {
      const container = renderShape(circle, {
        editEnabled: true,
        selectedResourceId: 'station-1',
      });
      const handles = container.querySelectorAll('[data-resize-handle]');

      // A rectangle has two and a polygon one per vertex; a circle has only its radius to change.
      expect(handles).toHaveLength(1);
      expect(handles[0]).toHaveAttribute('data-handle-index', '1');
      expect(handles[0]).toHaveAttribute('cx', '230');
    });

    it('changes the radius during a resize without moving the centre', () => {
      // The whole reason circle cannot reuse the polygon branch's coordinate substitution: the
      // centre must survive a drag of the rim, or the shape wanders while being resized.
      const container = renderShape(circle, {
        editEnabled: true,
        selectedResourceId: 'station-1',
        resizePreview: { handleIndex: 1, mousePosition: { x: 300, y: 200 } },
      });
      const shape = container.querySelector('circle');

      expect(shape).toHaveAttribute('cx', '200');
      expect(shape).toHaveAttribute('cy', '200');
      expect(shape).toHaveAttribute('r', '100');
    });
  });
});
