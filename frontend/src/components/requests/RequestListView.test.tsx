import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router';
import userEvent from '@testing-library/user-event';
import { TooltipProvider } from '@foundation/src/components/ui/tooltip';
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
    assignments: [],
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
    status: 'new',
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
  onNavigateToParent: vi.fn(),
};

function renderListView(
  props: Partial<React.ComponentProps<typeof RequestListView>> = {},
) {
  return render(
    <MemoryRouter>
    <TooltipProvider>
      <RequestListView
        requests={allRequests}
        selectedId={null}
        {...defaultHandlers}
        {...props}
      />
    </TooltipProvider>
    </MemoryRouter>,
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
    // Actions column header renders null (icon-only column)
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
    const badges = screen.getAllByText('New');
    expect(badges).toHaveLength(3);
  });

  it('calls onSelect and onEdit when clicking a row (row click opens the dialog)', () => {
    renderListView();
    fireEvent.click(screen.getByText('Child Task'));
    expect(defaultHandlers.onSelect).toHaveBeenCalledWith('child-1');
    expect(defaultHandlers.onEdit).toHaveBeenCalledWith(childReq);
  });

  it('renders empty when no requests', () => {
    renderListView({ requests: [] });
    // OrkyoDataTable hides the table when empty — assert the empty message instead
    expect(screen.getByText('No requests found.')).toBeInTheDocument();
    expect(screen.queryByText('Parent Group')).not.toBeInTheDocument();
  });

  it('shows "Unscheduled" for requests without dates', () => {
    renderListView();
    const unscheduled = screen.getAllByText('Unscheduled');
    // parent and child have no dates
    expect(unscheduled.length).toBeGreaterThanOrEqual(2);
  });

  it('clicking the inline Edit button stops propagation (onSelect not called)', async () => {
    const user = userEvent.setup();
    renderListView();
    await user.click(screen.getByRole('button', { name: 'Edit Child Task' }));
    expect(defaultHandlers.onSelect).not.toHaveBeenCalled();
  });

  it('clicking the inline Edit button calls onEdit', async () => {
    const user = userEvent.setup();
    renderListView();
    await user.click(screen.getByRole('button', { name: 'Edit Child Task' }));
    expect(defaultHandlers.onEdit).toHaveBeenCalledWith(childReq);
  });

  it('clicking the inline Delete button calls onDelete', async () => {
    const user = userEvent.setup();
    renderListView();
    await user.click(screen.getByRole('button', { name: 'Delete Child Task' }));
    expect(defaultHandlers.onDelete).toHaveBeenCalledWith(childReq);
  });

  it('does not render a "Move to" action', () => {
    renderListView();
    expect(screen.queryByText(/Move to/i)).not.toBeInTheDocument();
  });

  it('navigates to the parent when the Parent cell button is clicked', () => {
    renderListView();
    fireEvent.click(screen.getByRole('button', { name: 'Parent Group' }));
    expect(defaultHandlers.onNavigateToParent).toHaveBeenCalledWith('parent-1');
  });

  it('shows derived (italic) schedule/duration for a group with scheduled children', () => {
    const group = makeRequest({ id: 'g1', name: 'Group X', planningMode: 'summary' });
    const child = makeRequest({
      id: 'c1',
      name: 'Child X',
      parentRequestId: 'g1',
      startTs: '2026-03-01T09:00:00Z',
      endTs: '2026-03-01T17:00:00Z',
    });
    const { container } = renderListView({ requests: [group, child] });
    // The group rolls up its children's window/effort instead of showing
    // "Unscheduled" + its own (empty) minimal duration.
    expect(screen.queryByText('Unscheduled')).not.toBeInTheDocument();
    expect(container.querySelectorAll('.italic').length).toBeGreaterThan(0);
  });

  it('status header facet lists the badge labels, not raw wire values', async () => {
    const user = userEvent.setup();
    const inProgress = makeRequest({ id: 'ip-1', name: 'Rolling Task', status: 'in_progress' });
    renderListView({ requests: [standaloneReq, inProgress] });
    await user.click(screen.getByRole('button', { name: 'Status — sort and filter' }));
    const facets = screen.getByRole('group', { name: 'Filter Status' });
    expect(within(facets).getByText('New')).toBeInTheDocument();
    expect(within(facets).getByText('In Progress')).toBeInTheDocument();
    expect(within(facets).queryByText('in_progress')).not.toBeInTheDocument();
  });

  it('sorting Duration orders by derived minutes, not the display string', async () => {
    const user = userEvent.setup();
    // Alphabetically "2 hours" < "30 minutes", numerically 120 > 30 — the two
    // orders disagree, so this catches a string-based sort.
    const short = makeRequest({ id: 'short', name: 'Short Task', minimalDurationValue: 30, minimalDurationUnit: 'minutes' });
    const long = makeRequest({ id: 'long', name: 'Long Task', minimalDurationValue: 2, minimalDurationUnit: 'hours' });
    renderListView({ requests: [long, short] });
    await user.click(screen.getByRole('button', { name: 'Duration — sort and filter' }));
    await user.click(screen.getByRole('button', { name: 'Low → High' }));
    const bodyRows = screen.getAllByRole('row').slice(1); // drop the header row
    expect(bodyRows[0]).toHaveTextContent('Short Task');
    expect(bodyRows[1]).toHaveTextContent('Long Task');
  });

  // Icon plumbing
  it('renders the curated icon when request.icon is set', () => {
    const withIcon = makeRequest({ id: 'with-icon', name: 'With Icon', icon: 'calendar' });
    const { container } = renderListView({ requests: [withIcon] });
    expect(container.querySelector('.lucide-calendar')).not.toBeNull();
  });

  it('falls back to the planning-mode icon when request.icon is absent', () => {
    const leaf = makeRequest({ id: 'no-icon', name: 'No Icon', planningMode: 'leaf' });
    const { container } = renderListView({ requests: [leaf] });
    // Concrete proof of fallback: the planning-mode-derived svg renders.
    expect(container.querySelectorAll('svg').length).toBeGreaterThan(0);
    expect(container.querySelector('.lucide-calendar')).toBeNull();
  });

  it('filters from the Name header, matching the description too', async () => {
    // The page's search box is tree-only, so this header inherits its reach: it matches the
    // description, which no column displays.
    renderListView({
      requests: [
        makeRequest({ id: 'r1', name: 'Alpha', description: 'red apple', planningMode: 'leaf' }),
        makeRequest({ id: 'r2', name: 'Beta', description: 'blue sky', planningMode: 'leaf' }),
      ],
    });

    await userEvent.click(screen.getByRole('button', { name: 'Name — sort and filter' }));
    await userEvent.type(screen.getByLabelText('Filter Name'), 'apple');

    expect(screen.getByText('Alpha')).toBeInTheDocument();
    expect(screen.queryByText('Beta')).not.toBeInTheDocument();
  });

});
