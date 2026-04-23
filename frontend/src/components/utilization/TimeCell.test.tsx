import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render } from '@testing-library/react';
import type { Space } from '@foundation/src/types/space';
import type { TimeColumn } from './scheduler-types';

// Mock @dnd-kit/core's useDroppable so we control isOver and capture registration
const mockUseDroppable = vi.fn();
vi.mock('@dnd-kit/core', () => ({
  useDroppable: (args: unknown) => mockUseDroppable(args),
}));

// Import AFTER the mock so the component picks up our mocked hook
import { TimeCell } from './TimeCell';

const baseSpace: Space = {
  id: 'space-1',
  siteId: 'site-1',
  name: 'Room A',
  isPhysical: true,
  capacity: 1,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

function makeColumn(overrides: Partial<TimeColumn> = {}): TimeColumn {
  return {
    start: new Date('2026-01-01T08:00:00Z'),
    end: new Date('2026-01-01T09:00:00Z'),
    label: '08:00',
    ...overrides,
  };
}

function renderCell({
  isOver = false,
  isOutsideWorkingHours = false,
}: { isOver?: boolean; isOutsideWorkingHours?: boolean } = {}) {
  mockUseDroppable.mockReturnValue({ isOver, setNodeRef: vi.fn() });
  const column = makeColumn({ isOutsideWorkingHours });
  const { container } = render(
    <TimeCell
      column={column}
      space={baseSpace}
      timeCursorTs={new Date('2026-01-01T08:30:00Z')}
      requests={[]}
      onRequestClick={vi.fn()}
    />,
  );
  return { container, column };
}

describe('TimeCell', () => {
  beforeEach(() => {
    mockUseDroppable.mockReset();
  });

  it('registers a droppable scoped to space + column start', () => {
    const { column } = renderCell();
    expect(mockUseDroppable).toHaveBeenCalledTimes(1);
    expect(mockUseDroppable).toHaveBeenCalledWith({
      id: `space-1-${column.start.getTime()}`,
      data: { spaceId: 'space-1', startTs: column.start },
    });
  });

  it('renders neutral background when not over and inside working hours', () => {
    const { container } = renderCell();
    const cell = container.firstChild as HTMLElement;
    expect(cell.className).not.toMatch(/bg-blue-100/);
    expect(cell.className).not.toMatch(/bg-muted\/80/);
  });

  it('applies hover background when isOver is true', () => {
    const { container } = renderCell({ isOver: true });
    const cell = container.firstChild as HTMLElement;
    expect(cell.className).toMatch(/bg-blue-100/);
  });

  it('applies muted background outside working hours when not hovered', () => {
    const { container } = renderCell({ isOutsideWorkingHours: true });
    const cell = container.firstChild as HTMLElement;
    expect(cell.className).toMatch(/bg-muted\/80/);
  });

  it('hover background takes precedence over outside-working-hours background', () => {
    const { container } = renderCell({ isOver: true, isOutsideWorkingHours: true });
    const cell = container.firstChild as HTMLElement;
    expect(cell.className).toMatch(/bg-blue-100/);
    expect(cell.className).not.toMatch(/bg-muted\/80/);
  });
});
