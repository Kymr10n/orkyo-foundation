import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { useUiActionsStore } from '@foundation/src/store/ui-actions-store';
import { createTestQueryClient } from '@foundation/src/test-utils';
import { machineResourceType } from '@foundation/src/test-utils/resource-fixtures';
import type { ResourceStatusInfo } from '@foundation/src/lib/api/resource-status-api';
import { ResourceStatusSheet } from './ResourceStatusSheet';

const statusApi = vi.hoisted(() => ({ getResourceStatus: vi.fn() }));
vi.mock('@foundation/src/lib/api/resource-status-api', () => statusApi);

const typesApi = vi.hoisted(() => ({ getResourceTypes: vi.fn() }));
vi.mock('@foundation/src/lib/api/resource-types-api', () => typesApi);

const STATUS: ResourceStatusInfo = {
  resourceId: 'r-drill',
  name: 'Drill 3',
  resourceTypeKey: machineResourceType.key,
  isActive: true,
  asOfUtc: '2026-09-24T10:00:00Z',
  current: {
    assignmentId: 'a1',
    requestId: 'q1',
    requestName: 'Bracket batch',
    startUtc: '2026-09-24T08:00:00Z',
    endUtc: '2026-09-24T12:00:00Z',
  },
  next: null,
  activeAbsence: null,
  conflictCount: 0,
  lookAheadDays: 30,
  utilizationPercent: 41.7,
  utilizationDays: 30,
};

function Location() {
  const location = useLocation();
  return <span data-testid="location">{location.pathname + location.search}</span>;
}

function renderSheet() {
  const { wrapper: Wrapper } = createTestQueryClient();
  return render(
    <Wrapper>
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="*" element={<><ResourceStatusSheet /><Location /></>} />
        </Routes>
      </MemoryRouter>
    </Wrapper>,
  );
}

describe('ResourceStatusSheet', () => {
  beforeEach(() => {
    typesApi.getResourceTypes.mockResolvedValue([machineResourceType]);
    statusApi.getResourceStatus.mockResolvedValue(STATUS);
    useUiActionsStore.setState({ statusResourceId: 'r-drill' });
  });

  afterEach(() => {
    vi.clearAllMocks();
    vi.mocked(useCanEdit).mockReturnValue(true);
  });

  it('renders nothing while no resource is selected', () => {
    useUiActionsStore.setState({ statusResourceId: null });
    renderSheet();

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(statusApi.getResourceStatus).not.toHaveBeenCalled();
  });

  it('shows the current booking and the summary figures', async () => {
    renderSheet();

    expect(await screen.findByText('Drill 3')).toBeInTheDocument();
    expect(screen.getByText('Bracket batch')).toBeInTheDocument();
    expect(screen.getByText('No booking in the next 30 days')).toBeInTheDocument();
    expect(screen.getByText('Available')).toBeInTheDocument();
    expect(screen.getByText('None')).toBeInTheDocument();
    expect(screen.getByText('41.7%')).toBeInTheDocument();
    expect(statusApi.getResourceStatus).toHaveBeenCalledWith('r-drill');
  });

  it('shows an absence, conflicts, an inactive resource and missing utilization', async () => {
    statusApi.getResourceStatus.mockResolvedValue({
      ...STATUS,
      isActive: false,
      current: null,
      next: { ...STATUS.current!, requestName: '' },
      activeAbsence: { id: 'ab', title: 'Service', absenceType: 'maintenance', endTs: '2026-09-25T10:00:00Z' },
      conflictCount: 2,
      utilizationPercent: null,
    });
    renderSheet();

    expect(await screen.findByText('Inactive')).toBeInTheDocument();
    expect(screen.getByText('Not booked')).toBeInTheDocument();
    expect(screen.getByText('Untitled request')).toBeInTheDocument();
    expect(screen.getByText(/^Service until/)).toBeInTheDocument();
    expect(screen.getByText('2')).toBeInTheDocument();
    expect(screen.getByText('No data')).toBeInTheDocument();
  });

  it('takes an Editor to the edit dialog through the deep link, and closes', async () => {
    const user = userEvent.setup();
    renderSheet();

    await user.click(await screen.findByRole('button', { name: 'Edit' }));

    expect(screen.getByTestId('location')).toHaveTextContent(`/assets/${machineResourceType.key}/instances?edit=r-drill`);
    expect(useUiActionsStore.getState().statusResourceId).toBeNull();
  });

  it('offers a Viewer no Edit button', async () => {
    vi.mocked(useCanEdit).mockReturnValue(false);
    renderSheet();

    expect(await screen.findByText('Drill 3')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument();
  });

  it('says when the status cannot be loaded', async () => {
    statusApi.getResourceStatus.mockRejectedValue(new Error('500'));
    renderSheet();

    expect(await screen.findByText('The status of this resource could not be loaded.')).toBeInTheDocument();
  });

  it('closes from its own close button', async () => {
    const user = userEvent.setup();
    renderSheet();

    await user.click(await screen.findByRole('button', { name: 'Close' }));

    expect(useUiActionsStore.getState().statusResourceId).toBeNull();
  });
});
