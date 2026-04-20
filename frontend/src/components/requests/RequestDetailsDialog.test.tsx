import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { RequestDetailsDialog } from './RequestDetailsDialog';
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

const scheduledRequest = makeRequest({
  id: 'sched-1',
  name: 'Scheduled Task',
  description: 'A scheduled task',
  startTs: '2026-03-01T09:00:00Z',
  endTs: '2026-03-01T17:00:00Z',
  status: 'in_progress',
});

const constrainedRequest = makeRequest({
  id: 'constrained-1',
  name: 'Constrained Task',
  earliestStartTs: '2026-02-01T00:00:00Z',
  latestEndTs: '2026-04-01T00:00:00Z',
});

const withRequirements = makeRequest({
  id: 'req-1',
  name: 'Task With Reqs',
  requirements: [
    {
      id: 'rr-1',
      requestId: 'req-1',
      criterionId: 'c-1',
      value: 50,
      criterion: { id: 'c-1', name: 'Floor Area', dataType: 'Number', unit: 'm²' },
    },
    {
      id: 'rr-2',
      requestId: 'req-1',
      criterionId: 'c-2',
      value: true,
      criterion: { id: 'c-2', name: 'Wheelchair Access', dataType: 'Boolean' },
    },
  ],
});

const withSpace = makeRequest({
  id: 'space-1',
  name: 'Space Task',
  spaceId: 'sp-abc',
});

const withActualDuration = makeRequest({
  id: 'actual-dur',
  name: 'Actual Duration Task',
  startTs: '2026-03-01T09:00:00Z',
  endTs: '2026-03-03T17:00:00Z',
  actualDurationValue: 2880,
  actualDurationUnit: 'minutes',
});

const defaultProps = {
  open: true,
  onOpenChange: vi.fn(),
  request: scheduledRequest,
};

function renderDialog(props: Partial<React.ComponentProps<typeof RequestDetailsDialog>> = {}) {
  return render(<RequestDetailsDialog {...defaultProps} {...props} />);
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('RequestDetailsDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders nothing when request is null', () => {
    const { container } = render(
      <RequestDetailsDialog open={true} onOpenChange={vi.fn()} request={null} />,
    );
    expect(container.innerHTML).toBe('');
  });

  it('renders request name and status badge', () => {
    renderDialog();
    expect(screen.getByText('Scheduled Task')).toBeInTheDocument();
    expect(screen.getByText('in_progress')).toBeInTheDocument();
  });

  it('renders description when present', () => {
    renderDialog();
    expect(screen.getByText('A scheduled task')).toBeInTheDocument();
  });

  it('does not render description section when absent', () => {
    const noDesc = makeRequest({ name: 'No Desc' });
    renderDialog({ request: noDesc });
    expect(screen.queryByText('Description')).not.toBeInTheDocument();
  });

  it('shows schedule dates for scheduled request', () => {
    renderDialog();
    expect(screen.getByText('Start:')).toBeInTheDocument();
    expect(screen.getByText('End:')).toBeInTheDocument();
  });

  it('shows "Not set" for unscheduled request dates', () => {
    const unscheduled = makeRequest({ name: 'Unscheduled' });
    renderDialog({ request: unscheduled });
    const notSetElements = screen.getAllByText('Not set');
    expect(notSetElements.length).toBeGreaterThanOrEqual(2);
  });

  it('renders duration info', () => {
    renderDialog();
    expect(screen.getByText('Duration')).toBeInTheDocument();
    expect(screen.getByText(/working time/)).toBeInTheDocument();
  });

  it('renders actual duration when present', () => {
    renderDialog({ request: withActualDuration });
    expect(screen.getByText(/total \(incl\. off-times\)/)).toBeInTheDocument();
  });

  it('renders constraints section when set', () => {
    renderDialog({ request: constrainedRequest });
    expect(screen.getByText('Constraints')).toBeInTheDocument();
    expect(screen.getByText('Earliest Start:')).toBeInTheDocument();
    expect(screen.getByText('Latest End:')).toBeInTheDocument();
  });

  it('does not render constraints when not set', () => {
    renderDialog();
    expect(screen.queryByText('Constraints')).not.toBeInTheDocument();
  });

  it('renders requirements with criterion details', () => {
    renderDialog({ request: withRequirements });
    expect(screen.getByText(/Requirements \(2\)/)).toBeInTheDocument();
    expect(screen.getByText('Floor Area')).toBeInTheDocument();
    expect(screen.getByText('Wheelchair Access')).toBeInTheDocument();
    expect(screen.getByText('m²')).toBeInTheDocument();
  });

  it('formats boolean requirement as Yes/No', () => {
    renderDialog({ request: withRequirements });
    expect(screen.getByText('Yes')).toBeInTheDocument();
  });

  it('does not render requirements when empty', () => {
    renderDialog();
    expect(screen.queryByText(/Requirements/)).not.toBeInTheDocument();
  });

  it('renders assigned space when present', () => {
    renderDialog({ request: withSpace });
    expect(screen.getByText('Assigned Space')).toBeInTheDocument();
    expect(screen.getByText('sp-abc')).toBeInTheDocument();
  });

  it('does not render space section when not assigned', () => {
    renderDialog();
    expect(screen.queryByText('Assigned Space')).not.toBeInTheDocument();
  });

  it('calls onOpenChange(false) when Close button is clicked', () => {
    renderDialog();
    const closeButtons = screen.getAllByRole('button', { name: 'Close' });
    fireEvent.click(closeButtons[0]);
    expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
  });
});
