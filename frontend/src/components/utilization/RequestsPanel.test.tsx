import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { RequestsPanel } from './RequestsPanel';
import type { Request } from '@/types/requests';
import { DndContext } from '@dnd-kit/core';

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

const unscheduledLeaf = makeRequest({
  id: 'u-1',
  name: 'Unscheduled Task',
  planningMode: 'leaf',
  status: 'planned',
});

const scheduledLeaf = makeRequest({
  id: 's-1',
  name: 'Scheduled Task',
  planningMode: 'leaf',
  spaceId: 'space-1',
  startTs: '2026-03-01T09:00:00Z',
  status: 'in_progress',
});

const parentReq = makeRequest({
  id: 'p-1',
  name: 'Container Group',
  planningMode: 'container',
  status: 'planned',
});

const childOfParent = makeRequest({
  id: 'c-1',
  name: 'Child of Container',
  planningMode: 'leaf',
  parentRequestId: 'p-1',
  status: 'planned',
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
    expect(screen.getByText('Loading requests...')).toBeInTheDocument();
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
    const plannedBadges = screen.getAllByText('planned');
    expect(plannedBadges.length).toBeGreaterThanOrEqual(1);
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
});
