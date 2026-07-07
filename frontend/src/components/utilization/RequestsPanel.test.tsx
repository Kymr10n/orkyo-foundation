import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { RequestsPanel } from './RequestsPanel';
import type { Request } from '@foundation/src/types/requests';
import { DndContext } from '@dnd-kit/core';
import { spaceAssignment } from '@foundation/src/test-utils/request-fixtures';

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

const unscheduledLeaf = makeRequest({
  id: 'u-1',
  name: 'Unscheduled Task',
  planningMode: 'leaf',
  status: 'new',
});

const scheduledLeaf = makeRequest({
  id: 's-1',
  name: 'Scheduled Task',
  planningMode: 'leaf',
  assignments: [spaceAssignment('space-1')],
  startTs: '2026-03-01T09:00:00Z',
  status: 'in_progress',
  isScheduled: true,
});

const parentReq = makeRequest({
  id: 'p-1',
  name: 'Container Group',
  planningMode: 'container',
  status: 'new',
});

const childOfParent = makeRequest({
  id: 'c-1',
  name: 'Child of Container',
  planningMode: 'leaf',
  parentRequestId: 'p-1',
  status: 'new',
});

const doneRequest = makeRequest({
  id: 'd-1',
  name: 'Done Task',
  planningMode: 'leaf',
  status: 'done',
});

const allRequests = [unscheduledLeaf, scheduledLeaf, parentReq, childOfParent, doneRequest];

function renderPanel(
  requests: Request[] = allRequests,
  props: Partial<React.ComponentProps<typeof RequestsPanel>> = {},
) {
  return render(
    <DndContext>
      <RequestsPanel requests={requests} {...props} />
    </DndContext>,
  );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('RequestsPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders panel header', () => {
    renderPanel();
    expect(screen.getByText('Requests')).toBeInTheDocument();
  });

  it('shows loading state', () => {
    renderPanel([], { isLoading: true });
    expect(screen.getByText('Loading requests…')).toBeInTheDocument();
  });

  it('shows empty state when no requests match', () => {
    renderPanel([]);
    expect(screen.getByText('No requests found')).toBeInTheDocument();
  });

  // --- Default filter: unscheduled only ---

  it('shows unscheduled requests by default', () => {
    renderPanel();
    expect(screen.getByText('Unscheduled Task')).toBeInTheDocument();
  });

  it('hides scheduled requests by default (unscheduled filter)', () => {
    renderPanel();
    expect(screen.queryByText('Scheduled Task')).not.toBeInTheDocument();
  });

  it('shows container requests even in unscheduled mode', () => {
    renderPanel();
    expect(screen.getByText('Container Group')).toBeInTheDocument();
  });

  // --- Search ---

  it('renders search input', () => {
    renderPanel();
    expect(screen.getByPlaceholderText('Search requests...')).toBeInTheDocument();
    // Placeholder-only inputs need an accessible name for screen readers.
    expect(screen.getByRole('textbox', { name: 'Search requests' })).toBeInTheDocument();
  });

  it('filters by search query', () => {
    renderPanel();
    const search = screen.getByPlaceholderText('Search requests...');
    fireEvent.change(search, { target: { value: 'Container' } });
    expect(screen.getByText('Container Group')).toBeInTheDocument();
    expect(screen.queryByText('Unscheduled Task')).not.toBeInTheDocument();
  });

  // --- Status filter ---

  it('renders status filter dropdown', () => {
    renderPanel();
    expect(screen.getByText('All Statuses')).toBeInTheDocument();
  });

  // --- Schedule filter ---

  it('renders schedule filter dropdown', () => {
    renderPanel();
    expect(screen.getByText('Unscheduled Only')).toBeInTheDocument();
  });

  // --- Tree structure ---

  it('renders child requests indented under parent', () => {
    renderPanel();
    // Both container and child should render
    expect(screen.getByText('Container Group')).toBeInTheDocument();
    expect(screen.getByText('Child of Container')).toBeInTheDocument();
  });

  it('shows request status badge', () => {
    renderPanel();
    const statusBadges = screen.getAllByText('New');
    expect(statusBadges.length).toBeGreaterThanOrEqual(1);
  });

  it('shows "Unscheduled" indicator for unscheduled requests', () => {
    renderPanel();
    const unscheduledIndicators = screen.getAllByText('Unscheduled');
    expect(unscheduledIndicators.length).toBeGreaterThanOrEqual(1);
  });

  it('shows "Container" indicator for container nodes', () => {
    renderPanel();
    expect(screen.getByText('Container')).toBeInTheDocument();
  });

  it('shows request description when present', () => {
    const withDesc = makeRequest({ id: 'desc-1', name: 'With Desc', description: 'Hello world' });
    renderPanel([withDesc]);
    expect(screen.getByText('Hello world')).toBeInTheDocument();
  });

  it('renders in-progress badge, duration, and gross-duration for an unscheduled leaf', () => {
    const req = makeRequest({
      id: 'ip-1',
      name: 'In Progress Task',
      status: 'in_progress',
      durationMin: 120,
      actualDurationValue: 90,
      actualDurationUnit: 'minutes',
    });
    renderPanel([req]);
    expect(screen.getByText('In Progress Task')).toBeInTheDocument();
    expect(screen.getByText('In Progress')).toBeInTheDocument();
    expect(screen.getByText('• 2h')).toBeInTheDocument();
    expect(screen.getByText(/Gross:/)).toBeInTheDocument();
  });

  it('collapses and re-expands a parent node, pruning its children', () => {
    renderPanel([parentReq, childOfParent]);
    expect(screen.getByText('Child of Container')).toBeInTheDocument();

    // The expand/collapse toggle is the button inside the parent's card.
    const card = screen.getByText('Container Group').closest('[class*="rounded-lg"]')!;
    const toggle = () => card.querySelector('button')!;
    fireEvent.click(toggle());
    expect(screen.queryByText('Child of Container')).not.toBeInTheDocument();

    fireEvent.click(toggle());
    expect(screen.getByText('Child of Container')).toBeInTheDocument();
  });

  it('renders an "Add child request" button on drop-target nodes and fires onCreateChild', () => {
    const onCreateChild = vi.fn();
    renderPanel([parentReq], { onCreateChild });
    const addBtn = screen.getByTitle('Add child request');
    fireEvent.click(addBtn);
    expect(onCreateChild).toHaveBeenCalledWith('p-1');
  });

  // --- Card click → open editor ---

  it('fires onRequestClick with the request when a card is clicked', () => {
    const onRequestClick = vi.fn();
    renderPanel([unscheduledLeaf], { onRequestClick });
    fireEvent.click(screen.getByText('Unscheduled Task'));
    expect(onRequestClick).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'u-1' }),
    );
  });

  it('does not fire onRequestClick when the add-child button is clicked', () => {
    // Inner action buttons stop propagation so they never trigger the
    // card-level click that opens the request editor.
    const onRequestClick = vi.fn();
    const onCreateChild = vi.fn();
    renderPanel([parentReq], { onRequestClick, onCreateChild });
    fireEvent.click(screen.getByTitle('Add child request'));
    expect(onCreateChild).toHaveBeenCalledWith('p-1');
    expect(onRequestClick).not.toHaveBeenCalled();
  });

  it('does not fire onRequestClick when the collapse toggle is clicked', () => {
    const onRequestClick = vi.fn();
    renderPanel([parentReq, childOfParent], { onRequestClick });
    const card = screen.getByText('Container Group').closest('[class*="rounded-lg"]')!;
    fireEvent.click(card.querySelector('button')!);
    expect(onRequestClick).not.toHaveBeenCalled();
  });

  it('filters out requests that do not match the selected status', () => {
    renderPanel();
    const statusTrigger = screen.getByText('All Statuses');
    fireEvent.click(statusTrigger);
    fireEvent.click(screen.getByRole('option', { name: 'Done' }));
    expect(screen.getByText('Done Task')).toBeInTheDocument();
    expect(screen.queryByText('Unscheduled Task')).not.toBeInTheDocument();
  });

  it('shows scheduled requests with a green "Scheduled" indicator when the schedule filter is "Scheduled Only"', () => {
    renderPanel();
    const scheduleTrigger = screen.getByText('Unscheduled Only');
    fireEvent.click(scheduleTrigger);
    fireEvent.click(screen.getByRole('option', { name: 'Scheduled Only' }));
    expect(screen.getByText('Scheduled Task')).toBeInTheDocument();
    expect(screen.getByText('Scheduled')).toBeInTheDocument();
    expect(screen.queryByText('Unscheduled Task')).not.toBeInTheDocument();
  });

  // --- Non-drag "Schedule to…" action (WP-20) ---

  it('renders a "Schedule to…" action on an unscheduled leaf and fires onScheduleTo', () => {
    const onScheduleTo = vi.fn();
    renderPanel([unscheduledLeaf], { onScheduleTo });
    const btn = screen.getByRole('button', { name: 'Schedule Unscheduled Task' });
    fireEvent.click(btn);
    expect(onScheduleTo).toHaveBeenCalledWith(expect.objectContaining({ id: 'u-1' }));
  });

  it('does not render the "Schedule to…" action when onScheduleTo is not provided', () => {
    renderPanel([unscheduledLeaf]);
    expect(screen.queryByRole('button', { name: /^Schedule / })).not.toBeInTheDocument();
  });

  it('does not render the "Schedule to…" action on non-schedulable container nodes', () => {
    const onScheduleTo = vi.fn();
    renderPanel([parentReq], { onScheduleTo });
    expect(screen.queryByRole('button', { name: /^Schedule / })).not.toBeInTheDocument();
  });

  it('does not fire onRequestClick when the Schedule action is clicked (stops propagation)', () => {
    const onScheduleTo = vi.fn();
    const onRequestClick = vi.fn();
    renderPanel([unscheduledLeaf], { onScheduleTo, onRequestClick });
    fireEvent.click(screen.getByRole('button', { name: 'Schedule Unscheduled Task' }));
    expect(onScheduleTo).toHaveBeenCalledWith(expect.objectContaining({ id: 'u-1' }));
    expect(onRequestClick).not.toHaveBeenCalled();
  });

  // --- Card focusability / keyboard activation (WP-20) ---

  it('makes a clickable card keyboard-focusable and activatable via Enter/Space', () => {
    const onRequestClick = vi.fn();
    renderPanel([unscheduledLeaf], { onRequestClick });
    const card = screen.getByText('Unscheduled Task').closest('[class*="rounded-lg"]') as HTMLElement;
    expect(card).toHaveAttribute('tabindex', '0');
    expect(card).toHaveAttribute('role', 'button');

    fireEvent.keyDown(card, { key: 'Enter' });
    expect(onRequestClick).toHaveBeenCalledWith(expect.objectContaining({ id: 'u-1' }));

    fireEvent.keyDown(card, { key: ' ' });
    expect(onRequestClick).toHaveBeenCalledTimes(2);
  });

  it('does not make a card focusable when there is no click handler', () => {
    renderPanel([unscheduledLeaf]);
    const card = screen.getByText('Unscheduled Task').closest('[class*="rounded-lg"]') as HTMLElement;
    expect(card).not.toHaveAttribute('tabindex');
  });
});
