import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { RoutingSettings, describeSteps } from './RoutingSettings';
import { createTestQueryWrapper } from '@foundation/src/test-utils';
import { getRoutings, deleteRouting } from '@foundation/src/lib/api/routing-api';
import type { Routing } from '@foundation/src/types/routings';

const toastError = vi.fn();
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: (...a: unknown[]) => toastError(...a) } }));

vi.mock('@foundation/src/lib/api/routing-api', () => ({
  getRoutings: vi.fn(() => Promise.resolve([])),
  createRouting: vi.fn(),
  updateRouting: vi.fn(),
  deleteRouting: vi.fn(() => Promise.resolve()),
  instantiateRouting: vi.fn(),
}));

vi.mock('./RoutingEditDialog', () => ({
  RoutingEditDialog: ({ open, routing }: { open: boolean; routing: Routing | null }) =>
    open ? <div data-testid="routing-dialog">{routing ? routing.name : 'new'}</div> : null,
}));

const mockGetRoutings = vi.mocked(getRoutings);
const mockDeleteRouting = vi.mocked(deleteRouting);

const bracket: Routing = {
  id: 'r1',
  name: 'Bracket BR-100',
  description: 'Steel bracket',
  steps: [
    { id: 's2', stepNo: 2, operationTemplateId: 't2', operationName: 'Mill', setupMinutes: 30, runMinutesPerUnit: 60, lagMinutesAfter: 0 },
    { id: 's1', stepNo: 1, operationTemplateId: 't1', operationName: 'Saw', setupMinutes: 10, runMinutesPerUnit: 5, lagMinutesAfter: 0 },
    { id: 's3', stepNo: 3, operationTemplateId: 't3', operationName: 'Deburr', setupMinutes: 0, runMinutesPerUnit: 15, lagMinutesAfter: 0 },
  ],
};

function renderSettings() {
  return render(
    <MemoryRouter initialEntries={['/settings/routings']}><RoutingSettings /></MemoryRouter>,
    { wrapper: createTestQueryWrapper({ feedback: true }) },
  );
}

describe('describeSteps', () => {
  it('lists the operations in step order, whatever order they arrived in', () => {
    expect(describeSteps(bracket)).toBe('Saw → Mill → Deburr');
  });
});

describe('RoutingSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetRoutings.mockResolvedValue([]);
  });

  it('shows the empty state with a create affordance', async () => {
    renderSettings();
    expect(await screen.findByText('No routings defined yet')).toBeInTheDocument();
    expect(screen.getByText('Create your first routing')).toBeInTheDocument();
  });

  it('lists routings with their step sequence', async () => {
    mockGetRoutings.mockResolvedValue([bracket]);
    renderSettings();
    expect(await screen.findByText('Bracket BR-100')).toBeInTheDocument();
    expect(screen.getAllByText('Saw → Mill → Deburr').length).toBeGreaterThanOrEqual(1);
  });

  it('shows the error state and retries', async () => {
    const user = userEvent.setup();
    mockGetRoutings.mockRejectedValueOnce(new Error('Network error'));
    renderSettings();
    expect(await screen.findByText('Network error')).toBeInTheDocument();
    mockGetRoutings.mockResolvedValueOnce([bracket]);
    await user.click(screen.getByText('Try again'));
    expect(await screen.findByText('Bracket BR-100')).toBeInTheDocument();
  });

  it('opens the dialog in create mode from the header button', async () => {
    const user = userEvent.setup();
    renderSettings();
    await screen.findByText('No routings defined yet');
    await user.click(screen.getByRole('button', { name: /Add Routing/ }));
    expect(screen.getByTestId('routing-dialog')).toHaveTextContent('new');
  });

  it('opens the dialog in edit mode from the row action', async () => {
    const user = userEvent.setup();
    mockGetRoutings.mockResolvedValue([bracket]);
    renderSettings();
    await screen.findByText('Bracket BR-100');
    await user.click(screen.getAllByRole('button', { name: 'Edit Bracket BR-100' })[0]);
    expect(screen.getByTestId('routing-dialog')).toHaveTextContent('Bracket BR-100');
  });

  it('deletes after confirmation', async () => {
    const user = userEvent.setup();
    mockGetRoutings.mockResolvedValue([bracket]);
    renderSettings();
    await screen.findByText('Bracket BR-100');
    await user.click(screen.getAllByRole('button', { name: 'Delete Bracket BR-100' })[0]);
    expect(await screen.findByText('Delete "Bracket BR-100"?')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Delete' }));
    await waitFor(() => expect(mockDeleteRouting).toHaveBeenCalledWith('r1'));
  });

  it('does not delete when the confirmation is declined', async () => {
    const user = userEvent.setup();
    mockGetRoutings.mockResolvedValue([bracket]);
    renderSettings();
    await screen.findByText('Bracket BR-100');
    await user.click(screen.getAllByRole('button', { name: 'Delete Bracket BR-100' })[0]);
    await user.click(await screen.findByRole('button', { name: 'Cancel' }));
    expect(mockDeleteRouting).not.toHaveBeenCalled();
  });

  it('reports a failed delete through the central toast', async () => {
    const user = userEvent.setup();
    mockGetRoutings.mockResolvedValue([bracket]);
    mockDeleteRouting.mockRejectedValueOnce(new Error('In use'));
    renderSettings();
    await screen.findByText('Bracket BR-100');
    await user.click(screen.getAllByRole('button', { name: 'Delete Bracket BR-100' })[0]);
    await user.click(await screen.findByRole('button', { name: 'Delete' }));
    await waitFor(() => {
      expect(toastError).toHaveBeenCalledWith('Failed to delete routing', expect.objectContaining({ description: 'In use' }));
    });
  });
});
