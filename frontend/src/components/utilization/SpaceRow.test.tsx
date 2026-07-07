import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, fireEvent } from '@testing-library/react';
import type { Space } from '@foundation/src/types/space';
import type { Request } from '@foundation/src/types/requests';
import type { TimeColumn } from './scheduler-types';
import type { PreviewEntry, ValidationResult } from '@foundation/src/domain/scheduling/schedule-model';

// ---- Mocks for hooks and shared selectors ----
const mockUseSortable = vi.fn();
vi.mock('@dnd-kit/sortable', () => ({
  useSortable: (args: unknown) => mockUseSortable(args),
}));

const mockUseDroppable = vi.fn();
vi.mock('@dnd-kit/core', () => ({
  useDroppable: (args: unknown) => mockUseDroppable(args),
}));

vi.mock('@dnd-kit/utilities', () => ({
  CSS: { Transform: { toString: () => 'translate3d(0,0,0)' } },
}));

const mockSelectSpaceOverlapCount = vi.fn();
vi.mock('@foundation/src/domain/scheduling/schedule-selectors', () => ({
  selectSpaceOverlapCount: (...args: unknown[]) => mockSelectSpaceOverlapCount(...args),
  // Real implementation — SpaceRow uses it to window overlays to the view.
  isOutsideView: (entry: { startMs: number; endMs: number }, vs: number, ve: number) =>
    entry.endMs <= vs || entry.startMs >= ve,
}));

// ---- Stub child components so this test stays bounded to SpaceRow's own behavior ----
vi.mock('./ScheduledRequestOverlay', () => ({
  ScheduledRequestOverlay: ({
    request,
    onRequestClick,
  }: {
    request: Request;
    onRequestClick: (id: string) => void;
  }) => (
    <button
      type="button"
      data-testid={`overlay-${request.id}`}
      onClick={() => onRequestClick(request.id)}
    >
      {request.name}
    </button>
  ),
}));

import { SpaceRow } from './SpaceRow';
import { spaceAssignment } from '@foundation/src/test-utils/request-fixtures';

// ---- Fixtures ----
const baseSpace: Space = {
  id: 'space-1',
  siteId: 'site-1',
  name: 'Conference Room A',
  code: 'CRA',
  isPhysical: true,
  capacity: 4,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

function makeColumn(label: string): TimeColumn {
  return {
    start: new Date(`2026-01-01T${label}:00Z`),
    end: new Date(`2026-01-01T${label}:00Z`),
    label,
  };
}

function makeRequest(overrides: Partial<Request> = {}): Request {
  return {
    id: 'req-1',
    name: 'Request 1',
    parentRequestId: null,
    planningMode: 'manual' as Request['planningMode'],
    sortOrder: 0,
    assignments: [spaceAssignment('space-1')],
    minimalDurationValue: 60,
    minimalDurationUnit: 'minute' as Request['minimalDurationUnit'],
    schedulingSettingsApply: true,
    status: 'pending' as Request['status'],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

function makePreviewEntry(overrides: Partial<PreviewEntry> = {}): PreviewEntry {
  return {
    requestId: 'req-1',
    resourceId: 'space-1',
    startMs: Date.parse('2026-01-01T08:00:00Z'),
    endMs: Date.parse('2026-01-01T09:00:00Z'),
    name: 'Request 1',
    minimalDurationMs: 60 * 60 * 1000,
    isDraft: false,
    ...overrides,
  };
}

const emptyValidation: ValidationResult = new Map();

interface OffTimeRangeFixture {
  id: string;
  startMs: number;
  endMs: number;
  title: string;
  resourceIds: string[] | null;
}

function renderRow({
  spaceRequests = [],
  previewEntries = [],
  overlapCount = 1,
  isDragging = false,
  offTimeRanges = [],
  columns,
  onRequestClick = vi.fn(),
}: {
  spaceRequests?: Request[];
  previewEntries?: PreviewEntry[];
  overlapCount?: number;
  isDragging?: boolean;
  offTimeRanges?: OffTimeRangeFixture[];
  columns?: TimeColumn[];
  onRequestClick?: (id: string) => void;
} = {}) {
  mockUseSortable.mockReturnValue({
    attributes: { 'aria-roledescription': 'sortable' },
    listeners: { onPointerDown: vi.fn() },
    setNodeRef: vi.fn(),
    transform: null,
    transition: null,
    isDragging,
  });
  mockSelectSpaceOverlapCount.mockReturnValue(overlapCount);

  const result = render(
    <SpaceRow
      space={baseSpace}
      columns={columns ?? [makeColumn('08'), makeColumn('09'), makeColumn('10')]}
      spaceRequests={spaceRequests}
      spaceEntries={previewEntries}
      validation={emptyValidation}
      onRequestClick={onRequestClick}
      offTimeRanges={offTimeRanges as never}
    />,
  );
  return { ...result, onRequestClick };
}

// Cells are plain presentational divs (`min-w-[60px]`) rendered by TimelineRow.
function getCells(container: HTMLElement): HTMLElement[] {
  return Array.from(
    container.querySelectorAll<HTMLElement>('[class*="min-w-[60px]"]'),
  );
}

describe('SpaceRow', () => {
  beforeEach(() => {
    mockUseSortable.mockReset();
    mockSelectSpaceOverlapCount.mockReset();
    mockUseDroppable.mockReset();
    mockUseDroppable.mockReturnValue({ setNodeRef: vi.fn(), isOver: false });
  });

  it('registers a sortable for the space row', () => {
    renderRow();
    expect(mockUseSortable).toHaveBeenCalledTimes(1);
    expect(mockUseSortable).toHaveBeenCalledWith({
      id: 'space-1',
      data: { type: 'space-row' },
    });
  });

  it('registers a single row-level droppable carrying the column start times', () => {
    renderRow({ columns: [makeColumn('08'), makeColumn('09'), makeColumn('10')] });
    expect(mockUseDroppable).toHaveBeenCalledTimes(1);
    expect(mockUseDroppable).toHaveBeenCalledWith({
      id: 'track-space-1',
      data: {
        type: 'space-track',
        resourceId: 'space-1',
        columnStartsMs: [
          makeColumn('08').start.getTime(),
          makeColumn('09').start.getTime(),
          makeColumn('10').start.getTime(),
        ],
      },
    });
  });

  it('renders space code and name in the label column', () => {
    const { getByText } = renderRow();
    expect(getByText('CRA')).toBeInTheDocument();
    expect(getByText('Conference Room A')).toBeInTheDocument();
  });

  it('renders one cell per column', () => {
    const { container } = renderRow();
    expect(getCells(container)).toHaveLength(3);
  });

  it('renders ScheduledRequestOverlay for preview entries whose request is in spaceRequests', () => {
    const requestInSpace = makeRequest({ id: 'req-1', name: 'Mine' });
    const previewEntries = [
      makePreviewEntry({ requestId: 'req-1', resourceId: 'space-1' }),
      // entry whose request is absent from spaceRequests → skipped
      makePreviewEntry({ requestId: 'req-missing', resourceId: 'space-1' }),
    ];
    const { queryByTestId } = renderRow({
      spaceRequests: [requestInSpace],
      previewEntries,
    });
    expect(queryByTestId('overlay-req-1')).toBeInTheDocument();
    expect(queryByTestId('overlay-req-missing')).not.toBeInTheDocument();
  });

  it('does not render overlays for entries outside the visible time window', () => {
    // Default columns span [08:00, 10:00); this entry sits at 11:00–12:00.
    const { queryByTestId } = renderRow({
      spaceRequests: [makeRequest({ id: 'req-1' })],
      previewEntries: [
        makePreviewEntry({
          requestId: 'req-1',
          resourceId: 'space-1',
          startMs: Date.parse('2026-01-01T11:00:00Z'),
          endMs: Date.parse('2026-01-01T12:00:00Z'),
        }),
      ],
    });
    expect(queryByTestId('overlay-req-1')).not.toBeInTheDocument();
  });

  it('forwards request clicks from overlays to onRequestClick', () => {
    const onRequestClick = vi.fn();
    const { getByTestId } = renderRow({
      spaceRequests: [makeRequest({ id: 'req-1' })],
      previewEntries: [makePreviewEntry({ requestId: 'req-1', resourceId: 'space-1' })],
      onRequestClick,
    });
    fireEvent.click(getByTestId('overlay-req-1'));
    expect(onRequestClick).toHaveBeenCalledWith('req-1');
  });

  it('uses a single base row height when there are no overlaps', () => {
    const { container } = renderRow({ overlapCount: 1 });
    const cellsContainer = container.querySelector('[style*="min-height"]') as HTMLElement;
    expect(cellsContainer.style.minHeight).toBe('52px');
  });

  it('grows row height for additional overlapping requests', () => {
    const { container } = renderRow({ overlapCount: 3 });
    const cellsContainer = container.querySelector('[style*="min-height"]') as HTMLElement;
    // 52 + (3 - 1) * 44 = 140
    expect(cellsContainer.style.minHeight).toBe('140px');
  });

  it('reduces opacity when the row is being dragged', () => {
    const { container } = renderRow({ isDragging: true });
    const root = container.firstChild as HTMLElement;
    expect(root.style.opacity).toBe('0.5');
  });

  it('tints cells whose column overlaps an off-time range as off-time', () => {
    const cols: TimeColumn[] = [
      { start: new Date('2026-01-01T08:00:00Z'), end: new Date('2026-01-01T09:00:00Z'), label: '08' },
      { start: new Date('2026-01-01T09:00:00Z'), end: new Date('2026-01-01T10:00:00Z'), label: '09' },
      { start: new Date('2026-01-01T10:00:00Z'), end: new Date('2026-01-01T11:00:00Z'), label: '10' },
    ];
    const { container } = renderRow({
      columns: cols,
      offTimeRanges: [
        {
          id: 'ot-1',
          startMs: Date.parse('2026-01-01T09:00:00Z'),
          endMs: Date.parse('2026-01-01T10:00:00Z'),
          title: 'Lunch',
          resourceIds: ['space-1'],
        },
      ],
    });
    const cells = getCells(container);
    expect(cells[0].className).not.toMatch(/bg-destructive\/15/);
    expect(cells[1].className).toMatch(/bg-destructive\/15/);
    expect(cells[2].className).not.toMatch(/bg-destructive\/15/);
  });

  it('does not tint cells as off-time when the range applies to a different resource', () => {
    const cols: TimeColumn[] = [
      { start: new Date('2026-01-01T09:00:00Z'), end: new Date('2026-01-01T10:00:00Z'), label: '09' },
    ];
    const { container } = renderRow({
      columns: cols,
      offTimeRanges: [
        {
          id: 'ot-1',
          startMs: Date.parse('2026-01-01T09:00:00Z'),
          endMs: Date.parse('2026-01-01T10:00:00Z'),
          title: 'Other space',
          resourceIds: ['some-other-space'],
        },
      ],
    });
    expect(getCells(container)[0].className).not.toMatch(/bg-destructive\/15/);
  });
});
