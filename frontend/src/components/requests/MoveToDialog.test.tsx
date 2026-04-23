import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MoveToDialog } from './MoveToDialog';
import type { Request } from '@foundation/src/types/requests';

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

const target = makeRequest({
  id: 'target-1',
  name: 'Move Me',
  planningMode: 'leaf',
  parentRequestId: 'parent-1',
});

const currentParent = makeRequest({
  id: 'parent-1',
  name: 'Current Parent',
  planningMode: 'summary',
});

const otherParent = makeRequest({
  id: 'other-parent',
  name: 'Other Group',
  planningMode: 'summary',
});

const leafNode = makeRequest({
  id: 'leaf-node',
  name: 'Leaf (not a target)',
  planningMode: 'leaf',
});

const childOfTarget = makeRequest({
  id: 'child-of-target',
  name: 'Descendant',
  planningMode: 'leaf',
  parentRequestId: 'target-1',
});

const allRequests = [target, currentParent, otherParent, leafNode, childOfTarget];

const defaultProps = {
  open: true,
  onOpenChange: vi.fn(),
  request: target,
  allRequests,
  onConfirm: vi.fn(),
};

function renderDialog(props: Partial<React.ComponentProps<typeof MoveToDialog>> = {}) {
  return render(<MoveToDialog {...defaultProps} {...props} />);
}

describe('MoveToDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders dialog title with request name', () => {
    renderDialog();
    expect(screen.getByText(/Move "Move Me"/)).toBeInTheDocument();
  });

  it('shows "Root level" option when request has a parent', () => {
    renderDialog();
    expect(screen.getByText(/Root level/)).toBeInTheDocument();
  });

  it('hides "Root level" option when request is at root', () => {
    const rootRequest = makeRequest({ id: 'root-1', name: 'Root Req', parentRequestId: null });
    renderDialog({ request: rootRequest });
    expect(screen.queryByText(/Root level/)).not.toBeInTheDocument();
  });

  it('shows eligible parent candidates (excludes self, current parent, descendants, leaf nodes)', () => {
    renderDialog();
    // otherParent is a valid target (summary mode, not descendant)
    expect(screen.getByText('Other Group')).toBeInTheDocument();
    // currentParent is excluded (already the parent)
    expect(screen.queryByText('Current Parent')).not.toBeInTheDocument();
    // leafNode is excluded (can't have children)
    expect(screen.queryByText('Leaf (not a target)')).not.toBeInTheDocument();
  });

  it('filters candidates by search', () => {
    renderDialog();
    const searchInput = screen.getByPlaceholderText('Search targets...');
    fireEvent.change(searchInput, { target: { value: 'Other' } });
    expect(screen.getByText('Other Group')).toBeInTheDocument();
  });

  it('shows no results when search matches nothing', () => {
    const rootReq = makeRequest({ id: 'root-1', name: 'Root', parentRequestId: null });
    renderDialog({ request: rootReq, allRequests: [rootReq] });
    expect(screen.getByText('No eligible targets found.')).toBeInTheDocument();
  });

  it('selects a target and enables Move button', () => {
    renderDialog();
    fireEvent.click(screen.getByText('Other Group'));
    const moveButton = screen.getByRole('button', { name: 'Move' });
    expect(moveButton).not.toBeDisabled();
  });

  it('calls onConfirm with selected parent ID', () => {
    renderDialog();
    fireEvent.click(screen.getByText('Other Group'));
    fireEvent.click(screen.getByRole('button', { name: 'Move' }));
    expect(defaultProps.onConfirm).toHaveBeenCalledWith('other-parent');
  });

  it('calls onConfirm(null) when moving to root', () => {
    renderDialog();
    fireEvent.click(screen.getByText(/Root level/));
    fireEvent.click(screen.getByRole('button', { name: 'Move' }));
    expect(defaultProps.onConfirm).toHaveBeenCalledWith(null);
  });

  it('disables Move button when no selection and no parent', () => {
    const rootReq = makeRequest({ id: 'root-1', name: 'Root', parentRequestId: null, planningMode: 'leaf' });
    const container = makeRequest({ id: 'container-1', name: 'Container', planningMode: 'summary' });
    renderDialog({ request: rootReq, allRequests: [rootReq, container] });
    // Initially no selection, and root level option not shown, so Move should be disabled
    const moveButton = screen.getByRole('button', { name: 'Move' });
    expect(moveButton).toBeDisabled();
  });

  it('calls onOpenChange(false) when Cancel is clicked', () => {
    renderDialog();
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
  });
});
