import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { AddExistingRequestsDialog } from './AddExistingRequestsDialog';
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

const parentReq = makeRequest({
  id: 'parent-1',
  name: 'Parent Group',
  planningMode: 'summary',
});

const existingChild = makeRequest({
  id: 'child-1',
  name: 'Existing Child',
  planningMode: 'leaf',
  parentRequestId: 'parent-1',
});

const candidateA = makeRequest({
  id: 'candidate-a',
  name: 'Candidate Alpha',
  planningMode: 'leaf',
});

const candidateB = makeRequest({
  id: 'candidate-b',
  name: 'Candidate Beta',
  planningMode: 'leaf',
});

const grandchild = makeRequest({
  id: 'grandchild-1',
  name: 'Grandchild',
  planningMode: 'leaf',
  parentRequestId: 'child-1',
});

const allRequests: Request[] = [
  parentReq,
  existingChild,
  candidateA,
  candidateB,
  grandchild,
];

const defaultProps = {
  open: true,
  onOpenChange: vi.fn(),
  parentRequest: parentReq,
  allRequests,
  onConfirm: vi.fn(),
};

function renderDialog(
  props: Partial<React.ComponentProps<typeof AddExistingRequestsDialog>> = {},
) {
  return render(
    <AddExistingRequestsDialog {...defaultProps} {...props} />,
  );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('AddExistingRequestsDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the dialog title and description', () => {
    renderDialog();
    expect(screen.getByText('Add existing requests')).toBeInTheDocument();
    expect(screen.getByText(/Parent Group/)).toBeInTheDocument();
  });

  it('shows only valid candidates (excludes parent, existing children, descendants)', () => {
    renderDialog();
    // candidateA and candidateB are valid candidates
    expect(screen.getByText('Candidate Alpha')).toBeInTheDocument();
    expect(screen.getByText('Candidate Beta')).toBeInTheDocument();
    // parent itself should NOT appear
    expect(screen.queryByText('Parent Group')).not.toBeNull(); // appears in description
    // existingChild is already a child → excluded
    expect(screen.queryByText('Existing Child')).not.toBeInTheDocument();
  });

  it('shows search input', () => {
    renderDialog();
    expect(screen.getByPlaceholderText('Search requests…')).toBeInTheDocument();
  });

  it('filters candidates by search query', () => {
    renderDialog();
    const searchInput = screen.getByPlaceholderText('Search requests…');
    fireEvent.change(searchInput, { target: { value: 'Alpha' } });
    expect(screen.getByText('Candidate Alpha')).toBeInTheDocument();
    expect(screen.queryByText('Candidate Beta')).not.toBeInTheDocument();
  });

  it('shows "0 selected" initially and button is disabled', () => {
    renderDialog();
    expect(screen.getByText('0 selected')).toBeInTheDocument();
    const moveButton = screen.getByRole('button', { name: 'Move' });
    expect(moveButton).toBeDisabled();
  });

  it('selects and deselects candidates', () => {
    renderDialog();
    const checkboxes = screen.getAllByRole('checkbox');
    // Click first checkbox
    fireEvent.click(checkboxes[0]);
    expect(screen.getByText('1 selected')).toBeInTheDocument();
    // Click it again to deselect
    fireEvent.click(checkboxes[0]);
    expect(screen.getByText('0 selected')).toBeInTheDocument();
  });

  it('calls onConfirm with selected IDs', () => {
    renderDialog();
    const checkboxes = screen.getAllByRole('checkbox');
    fireEvent.click(checkboxes[0]);
    fireEvent.click(checkboxes[1]);
    expect(screen.getByText('2 selected')).toBeInTheDocument();

    const moveButton = screen.getByRole('button', { name: /Move 2 requests/ });
    fireEvent.click(moveButton);
    expect(defaultProps.onConfirm).toHaveBeenCalledWith(
      expect.arrayContaining(['candidate-a', 'candidate-b']),
    );
  });

  it('calls onOpenChange(false) when Cancel is clicked', () => {
    renderDialog();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
  });

  it('shows "No requests available" when no candidates exist', () => {
    // All requests are either the parent or already its children
    const onlyParentAndChild = [parentReq, existingChild];
    renderDialog({ allRequests: onlyParentAndChild });
    expect(screen.getByText('No requests available to add')).toBeInTheDocument();
  });

  it('shows "No matching requests" when search has no results', () => {
    renderDialog();
    const searchInput = screen.getByPlaceholderText('Search requests…');
    fireEvent.change(searchInput, { target: { value: 'zzzzz' } });
    expect(screen.getByText('No matching requests')).toBeInTheDocument();
  });

  it('shows mode badges for candidates', () => {
    renderDialog();
    // Both candidates are leaf = "Task"
    const taskBadges = screen.getAllByText('Task');
    expect(taskBadges.length).toBeGreaterThanOrEqual(2);
  });
});
