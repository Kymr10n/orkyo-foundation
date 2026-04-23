import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { RequestListView } from './RequestListView';
import type { Request } from '@foundation/src/types/requests';

// Mock virtualizer so all items render in jsdom (no DOM measurements)
vi.mock('@tanstack/react-virtual', () => ({
  useVirtualizer: vi.fn(({ count }: { count: number }) => ({
    getVirtualItems: () =>
      Array.from({ length: count }, (_, i) => ({
        index: i,
        start: i * 49,
        size: 49,
        key: i,
      })),
    getTotalSize: () => count * 49,
  })),
}));

// ---------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------

function makeRequest(overrides: Partial<Request> = {}): Request {
  return {
    id: 'r-1',
    name: 'Test Request',
    description: null,
    parentRequestId: null,
    planningMode: 'leaf',
    sortOrder: 0,
    spaceId: null,
    startTs: null,
    endTs: null,
    earliestStartTs: null,
    latestEndTs: null,
    minimalDurationValue: 60,
    minimalDurationUnit: 'minutes',
    actualDurationValue: null,
    actualDurationUnit: null,
    durationMin: undefined,
    schedulingSettingsApply: true,
    status: 'planned',
    requirements: [],
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

const parentReq = makeRequest({
  id: 'parent-1',
  name: 'Parent Group',
  planningMode: 'summary',
});

const childReq = makeRequest({
  id: 'child-1',
  name: 'Child Task',
  planningMode: 'leaf',
  parentRequestId: 'parent-1',
});

const standaloneReq = makeRequest({
  id: 'standalone-1',
  name: 'Standalone Task',
  planningMode: 'leaf',
  startTs: '2026-03-01T09:00:00Z',
  endTs: '2026-03-01T17:00:00Z',
});

const allRequests: Request[] = [parentReq, childReq, standaloneReq];

const defaultHandlers = {
  onSelect: vi.fn(),
  onEdit: vi.fn(),
  onDelete: vi.fn(),
  onAddChild: vi.fn(),
  onAddExisting: vi.fn(),
};

function renderListView(
  props: Partial<React.ComponentProps<typeof RequestListView>> = {},
) {
  return render(
    <RequestListView
      requests={allRequests}
      selectedId={null}
      {...defaultHandlers}
      {...props}
    />,
  );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('RequestListView', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders table headers', () => {
    renderListView();
    expect(screen.getByText('Name')).toBeInTheDocument();
    expect(screen.getByText('Kind')).toBeInTheDocument();
    expect(screen.getByText('Parent')).toBeInTheDocument();
    expect(screen.getByText('Schedule')).toBeInTheDocument();
    expect(screen.getByText('Duration')).toBeInTheDocument();
    expect(screen.getByText('Status')).toBeInTheDocument();
    expect(screen.getByText('Actions')).toBeInTheDocument();
  });

  it('renders all requests', () => {
    renderListView();
    // Use getAllByText since 'Parent Group' appears both as row name and parent column
    expect(screen.getAllByText('Parent Group').length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('Child Task')).toBeInTheDocument();
    expect(screen.getByText('Standalone Task')).toBeInTheDocument();
  });

  it('shows parent name for child request', () => {
    renderListView();
    // childReq has parentRequestId = parent-1, whose name is "Parent Group"
    // The parent name appears as a secondary text in the Parent column
    const parentCells = screen.getAllByText('Parent Group');
    // One for the row itself, one for the child's parent column
    expect(parentCells.length).toBeGreaterThanOrEqual(2);
  });

  it('shows dash for requests without parent', () => {
    renderListView();
    // Parent and standalone have no parent → show "—"
    const dashes = screen.getAllByText('—');
    expect(dashes.length).toBeGreaterThanOrEqual(2);
  });

  it('shows mode badges', () => {
    renderListView();
    expect(screen.getByText('Group')).toBeInTheDocument();
    expect(screen.getAllByText('Task')).toHaveLength(2);
  });

  it('shows status badges', () => {
    renderListView();
    const badges = screen.getAllByText('Planned');
    expect(badges).toHaveLength(3);
  });

  it('calls onSelect when clicking a row', () => {
    renderListView();
    fireEvent.click(screen.getByText('Child Task'));
    expect(defaultHandlers.onSelect).toHaveBeenCalledWith('child-1');
  });

  it('calls onEdit when double-clicking a row', () => {
    renderListView();
    fireEvent.doubleClick(screen.getByText('Standalone Task'));
    expect(defaultHandlers.onEdit).toHaveBeenCalledWith(standaloneReq);
  });

  it('renders empty when no requests', () => {
    renderListView({ requests: [] });
    expect(screen.getByText('Name')).toBeInTheDocument();
    // No data rows
    expect(screen.queryByText('Parent Group')).not.toBeInTheDocument();
  });

  it('shows "Unscheduled" for requests without dates', () => {
    renderListView();
    const unscheduled = screen.getAllByText('Unscheduled');
    // parent and child have no dates
    expect(unscheduled.length).toBeGreaterThanOrEqual(2);
  });
});
