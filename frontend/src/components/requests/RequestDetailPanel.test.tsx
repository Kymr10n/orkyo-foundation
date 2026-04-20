import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { RequestDetailPanel } from './RequestDetailPanel';
import type { Request } from '@/types/requests';

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

const leafRequest = makeRequest({
  id: 'leaf-1',
  name: 'Leaf Task',
  description: 'A simple leaf task',
  planningMode: 'leaf',
  minimalDurationValue: 120,
  minimalDurationUnit: 'minutes',
  status: 'planned',
});

const scheduledLeaf = makeRequest({
  id: 'scheduled-1',
  name: 'Scheduled Task',
  planningMode: 'leaf',
  spaceId: 'space-1',
  startTs: '2026-03-01T09:00:00Z',
  endTs: '2026-03-01T17:00:00Z',
});

const parentRequest = makeRequest({
  id: 'parent-1',
  name: 'Summary Group',
  planningMode: 'summary',
  status: 'in_progress',
});

const childA = makeRequest({
  id: 'child-a',
  name: 'Child A',
  planningMode: 'leaf',
  parentRequestId: 'parent-1',
  startTs: '2026-03-01T09:00:00Z',
  endTs: '2026-03-02T09:00:00Z',
  minimalDurationValue: 60,
  minimalDurationUnit: 'minutes',
});

const childB = makeRequest({
  id: 'child-b',
  name: 'Child B',
  planningMode: 'leaf',
  parentRequestId: 'parent-1',
  startTs: '2026-03-03T09:00:00Z',
  endTs: '2026-03-04T09:00:00Z',
  minimalDurationValue: 30,
  minimalDurationUnit: 'minutes',
});

const constrainedRequest = makeRequest({
  id: 'constrained-1',
  name: 'Constrained Task',
  planningMode: 'leaf',
  earliestStartTs: '2026-02-01T00:00:00Z',
  latestEndTs: '2026-04-01T00:00:00Z',
});

const withRequirements = makeRequest({
  id: 'req-1',
  name: 'Task With Requirements',
  planningMode: 'leaf',
  requirements: [
    {
      id: 'rr-1',
      requestId: 'req-1',
      criterionId: 'c-1',
      value: 42,
      criterion: { id: 'c-1', name: 'Floor Area', dataType: 'Number', unit: 'm²' },
    },
    {
      id: 'rr-2',
      requestId: 'req-1',
      criterionId: 'c-2',
      value: true,
      criterion: { id: 'c-2', name: 'Has WiFi', dataType: 'Boolean' },
    },
  ],
});

const allRequests = [leafRequest, parentRequest, childA, childB];

const defaultProps = {
  request: leafRequest,
  allRequests: [leafRequest],
  onEdit: vi.fn(),
  onNavigate: vi.fn(),
  onClose: vi.fn(),
};

function renderPanel(props: Partial<React.ComponentProps<typeof RequestDetailPanel>> = {}) {
  return render(<RequestDetailPanel {...defaultProps} {...props} />);
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('RequestDetailPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders request name and planning mode', () => {
    renderPanel();
    expect(screen.getByText('Leaf Task')).toBeInTheDocument();
    expect(screen.getByText('Task')).toBeInTheDocument();
  });

  it('renders description when present', () => {
    renderPanel();
    expect(screen.getByText('A simple leaf task')).toBeInTheDocument();
  });

  it('does not render description when absent', () => {
    const noDesc = makeRequest({ id: 'no-desc', name: 'No Desc', description: null });
    renderPanel({ request: noDesc, allRequests: [noDesc] });
    expect(screen.queryByText('A simple leaf task')).not.toBeInTheDocument();
  });

  it('shows "Not yet scheduled" for unscheduled request', () => {
    renderPanel();
    expect(screen.getByText('Not yet scheduled')).toBeInTheDocument();
  });

  it('shows start and end dates for scheduled request', () => {
    renderPanel({ request: scheduledLeaf, allRequests: [scheduledLeaf] });
    expect(screen.getByText('Start')).toBeInTheDocument();
    expect(screen.getByText('End')).toBeInTheDocument();
    expect(screen.queryByText('Not yet scheduled')).not.toBeInTheDocument();
  });

  it('renders duration info', () => {
    renderPanel();
    expect(screen.getByText('Minimal')).toBeInTheDocument();
    expect(screen.getByText('Duration')).toBeInTheDocument();
  });

  it('shows status badge', () => {
    renderPanel();
    expect(screen.getByText('planned')).toBeInTheDocument();
  });

  it('calls onEdit when edit button is clicked', () => {
    renderPanel();
    const editButton = screen.getAllByRole('button')[0];
    fireEvent.click(editButton);
    expect(defaultProps.onEdit).toHaveBeenCalledWith(leafRequest);
  });

  it('calls onClose when close button is clicked', () => {
    renderPanel();
    const buttons = screen.getAllByRole('button');
    const closeButton = buttons[1]; // second button is close
    fireEvent.click(closeButton);
    expect(defaultProps.onClose).toHaveBeenCalled();
  });

  // --- Parent/children ---

  it('renders children list for parent request', () => {
    renderPanel({ request: parentRequest, allRequests });
    expect(screen.getByText(/Children/)).toBeInTheDocument();
    expect(screen.getByText('Child A')).toBeInTheDocument();
    expect(screen.getByText('Child B')).toBeInTheDocument();
  });

  it('shows derived schedule for parent with scheduled children', () => {
    renderPanel({ request: parentRequest, allRequests });
    expect(screen.getByText('Derived from children')).toBeInTheDocument();
    expect(screen.getByText('Earliest')).toBeInTheDocument();
    expect(screen.getByText('Latest')).toBeInTheDocument();
  });

  it('shows sum of children duration for parent', () => {
    renderPanel({ request: parentRequest, allRequests });
    expect(screen.getByText('Sum of children')).toBeInTheDocument();
  });

  it('does not render children section for leaf request', () => {
    renderPanel();
    expect(screen.queryByText(/Children/)).not.toBeInTheDocument();
  });

  it('navigates to child when child is clicked', () => {
    renderPanel({ request: parentRequest, allRequests });
    fireEvent.click(screen.getByText('Child A'));
    expect(defaultProps.onNavigate).toHaveBeenCalledWith('child-a');
  });

  // --- Breadcrumb ---

  it('renders breadcrumb for nested request', () => {
    renderPanel({ request: childA, allRequests });
    expect(screen.getByText('Summary Group')).toBeInTheDocument();
  });

  it('navigates to ancestor via breadcrumb click', () => {
    renderPanel({ request: childA, allRequests });
    fireEvent.click(screen.getByText('Summary Group'));
    expect(defaultProps.onNavigate).toHaveBeenCalledWith('parent-1');
  });

  it('does not render breadcrumb for root request', () => {
    renderPanel({ request: parentRequest, allRequests });
    // The parent's name should appear in the header but not in breadcrumb navigation
    const breadcrumbButtons = screen.queryAllByRole('button').filter(
      (btn) => btn.textContent === 'Summary Group' && btn.classList.contains('hover:underline'),
    );
    expect(breadcrumbButtons).toHaveLength(0);
  });

  // --- Constraints ---

  it('renders constraints section when constraints are set', () => {
    renderPanel({ request: constrainedRequest, allRequests: [constrainedRequest] });
    expect(screen.getByText('Constraints')).toBeInTheDocument();
    expect(screen.getByText('Earliest start')).toBeInTheDocument();
    expect(screen.getByText('Latest end')).toBeInTheDocument();
  });

  it('does not render constraints section when no constraints', () => {
    renderPanel();
    expect(screen.queryByText('Constraints')).not.toBeInTheDocument();
  });

  // --- Requirements ---

  it('renders requirements when present', () => {
    renderPanel({ request: withRequirements, allRequests: [withRequirements] });
    const reqHeaders = screen.getAllByText(/Requirements/);
    expect(reqHeaders.length).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('Floor Area')).toBeInTheDocument();
    expect(screen.getByText('Has WiFi')).toBeInTheDocument();
  });

  it('does not render requirements section when empty', () => {
    renderPanel();
    expect(screen.queryByText(/Requirements/)).not.toBeInTheDocument();
  });
});
