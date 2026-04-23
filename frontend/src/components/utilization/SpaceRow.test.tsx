import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, fireEvent } from '@testing-library/react';
import type { Space } from '@foundation/src/types/space';
import type { Request } from '@foundation/src/types/requests';
import type { TimeColumn } from './scheduler-types';
import type { PreviewEntry, PreviewSchedule, ValidationResult } from '@foundation/src/domain/scheduling/schedule-model';
import type { ScheduleIndex } from '@foundation/src/domain/scheduling/schedule-index';

// ---- Mocks for hooks and shared selectors ----
const mockUseSortable = vi.fn();
vi.mock('@dnd-kit/sortable', () => ({
  useSortable: (args: unknown) => mockUseSortable(args),
}));

vi.mock('@dnd-kit/utilities', () => ({
  CSS: { Transform: { toString: () => 'translate3d(0,0,0)' } },
}));

const mockSelectSpaceOverlapCount = vi.fn();
vi.mock('@foundation/src/domain/scheduling/schedule-selectors', () => ({
  selectSpaceOverlapCount: (...args: unknown[]) => mockSelectSpaceOverlapCount(...args),
}));

// ---- Stub child components so this test stays bounded to SpaceRow's own behavior ----
vi.mock('./TimeCell', () => ({
  TimeCell: ({ column }: { column: TimeColumn }) => (
    <div data-testid="time-cell" data-label={column.label} />
  ),
}));

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

vi.mock('./OffTimeOverlay', () => ({
  OffTimeOverlay: ({ offTime }: { offTime: { id: string } }) => (
    <div data-testid={`offtime-${offTime.id}`} />
  ),
}));

import { SpaceRow } from './SpaceRow';

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
    spaceId: 'space-1',
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
    spaceId: 'space-1',
    startMs: Date.parse('2026-01-01T08:00:00Z'),
    endMs: Date.parse('2026-01-01T09:00:00Z'),
    name: 'Request 1',
    minimalDurationMs: 60 * 60 * 1000,
    isDraft: false,
    ...overrides,
  };
}

function buildIndex(entries: PreviewEntry[]): ScheduleIndex {
  const bySpace = new Map<string, PreviewEntry[]>();
  for (const e of entries) {
    const list = bySpace.get(e.spaceId) ?? [];
    list.push(e);
    bySpace.set(e.spaceId, list);
  }
  return { bySpace };
}

const emptyValidation: ValidationResult = new Map();
const emptyPreview: PreviewSchedule = new Map();

function renderRow({
  requests = [],
  previewEntries = [],
  overlapCount = 1,
  isDragging = false,
  offTimeRanges = [],
  onRequestClick = vi.fn(),
}: {
  requests?: Request[];
  previewEntries?: PreviewEntry[];
  overlapCount?: number;
  isDragging?: boolean;
  offTimeRanges?: { id: string; spaceId: string; startTs: string; endTs: string }[];
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
      columns={[makeColumn('08'), makeColumn('09'), makeColumn('10')]}
      requests={requests}
      previewSchedule={emptyPreview}
      scheduleIndex={buildIndex(previewEntries)}
      validation={emptyValidation}
      timeCursorTs={new Date('2026-01-01T08:30:00Z')}
      onRequestClick={onRequestClick}
      offTimeRanges={offTimeRanges as never}
    />,
  );
  return { ...result, onRequestClick };
}

describe('SpaceRow', () => {
  beforeEach(() => {
    mockUseSortable.mockReset();
    mockSelectSpaceOverlapCount.mockReset();
  });

  it('registers a sortable for the space row', () => {
    renderRow();
    expect(mockUseSortable).toHaveBeenCalledTimes(1);
    expect(mockUseSortable).toHaveBeenCalledWith({
      id: 'space-1',
      data: { type: 'space-row' },
    });
  });

  it('renders space code and name in the label column', () => {
    const { getByText } = renderRow();
    expect(getByText('CRA')).toBeInTheDocument();
    expect(getByText('Conference Room A')).toBeInTheDocument();
  });

  it('renders one TimeCell per column', () => {
    const { getAllByTestId } = renderRow();
    expect(getAllByTestId('time-cell')).toHaveLength(3);
  });

  it('renders ScheduledRequestOverlay only for preview entries whose request belongs to this space', () => {
    const requestInSpace = makeRequest({ id: 'req-1', name: 'Mine' });
    const requestOther = makeRequest({ id: 'req-other', name: 'Other', spaceId: 'space-2' });
    const previewEntries = [
      makePreviewEntry({ requestId: 'req-1', spaceId: 'space-1' }),
      // entry whose request is missing from the requests array → skipped
      makePreviewEntry({ requestId: 'req-missing', spaceId: 'space-1' }),
    ];
    const { queryByTestId } = renderRow({
      requests: [requestInSpace, requestOther],
      previewEntries,
    });
    expect(queryByTestId('overlay-req-1')).toBeInTheDocument();
    expect(queryByTestId('overlay-req-missing')).not.toBeInTheDocument();
    expect(queryByTestId('overlay-req-other')).not.toBeInTheDocument();
  });

  it('forwards request clicks from overlays to onRequestClick', () => {
    const onRequestClick = vi.fn();
    const { getByTestId } = renderRow({
      requests: [makeRequest({ id: 'req-1' })],
      previewEntries: [makePreviewEntry({ requestId: 'req-1', spaceId: 'space-1' })],
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

  it('renders an OffTimeOverlay for each provided off-time range', () => {
    const { getByTestId } = renderRow({
      offTimeRanges: [
        { id: 'ot-1', spaceId: 'space-1', startTs: '2026-01-01T12:00:00Z', endTs: '2026-01-01T13:00:00Z' },
        { id: 'ot-2', spaceId: 'space-1', startTs: '2026-01-01T17:00:00Z', endTs: '2026-01-01T18:00:00Z' },
      ],
    });
    expect(getByTestId('offtime-ot-1')).toBeInTheDocument();
    expect(getByTestId('offtime-ot-2')).toBeInTheDocument();
  });
});
