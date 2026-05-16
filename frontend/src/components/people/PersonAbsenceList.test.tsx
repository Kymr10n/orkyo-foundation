import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { PersonAbsenceList } from './PersonAbsenceList';
import type { ResourceInfo, ResourcesResponse } from '@foundation/src/lib/api/resources-api';
import type { ResourceAbsenceInfo } from '@foundation/src/lib/api/resource-absences-api';

vi.mock('@foundation/src/lib/api/site-api', () => ({
  getSites: vi.fn(),
}));

vi.mock('@foundation/src/lib/api/resources-api', () => ({
  getResources: vi.fn(),
}));

vi.mock('@foundation/src/lib/api/resource-absences-api', () => ({
  getResourceAbsences: vi.fn(),
  deleteResourceAbsence: vi.fn(),
  createResourceAbsence: vi.fn(),
}));

import { getSites } from '@foundation/src/lib/api/site-api';
import { getResources } from '@foundation/src/lib/api/resources-api';
import { getResourceAbsences, deleteResourceAbsence } from '@foundation/src/lib/api/resource-absences-api';

const mockSites = [
  { id: 'site-1', name: 'Main Office', code: 'MAIN' },
];

const mockPeople: ResourceInfo[] = [
  {
    id: 'person-alice',
    resourceTypeId: 'rt-person',
    resourceTypeKey: 'person',
    name: 'Alice',
    allocationMode: 'Exclusive',
    baseAvailabilityPercent: 100,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
];

const peopleResponse: ResourcesResponse = { data: mockPeople, total: 1, page: 1, pageSize: 50 };

const mockAbsences: ResourceAbsenceInfo[] = [
  {
    id: 'abs-1',
    siteId: 'site-1',
    title: 'Summer break',
    type: 'vacation',
    appliesToAllResources: false,
    resourceIds: ['person-alice'],
    startTs: '2026-07-01T00:00:00Z',
    endTs: '2026-07-14T00:00:00Z',
    isRecurring: false,
    enabled: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
  },
];

function renderList() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <PersonAbsenceList />
    </QueryClientProvider>,
  );
}

describe('PersonAbsenceList', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getSites).mockResolvedValue(mockSites as never);
    vi.mocked(getResources).mockResolvedValue(peopleResponse);
    vi.mocked(getResourceAbsences).mockResolvedValue(mockAbsences);
    vi.mocked(deleteResourceAbsence).mockResolvedValue(undefined);
  });

  it('shows site selector and Add Absence button', async () => {
    renderList();
    await waitFor(() => expect(getSites).toHaveBeenCalled());
    expect(screen.getByRole('combobox')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Add Absence/i })).toBeInTheDocument();
  });

  it('Add Absence button is disabled before site is selected', async () => {
    renderList();
    await waitFor(() => expect(getSites).toHaveBeenCalled());
    expect(screen.getByRole('button', { name: /Add Absence/i })).toBeDisabled();
  });

  it('shows prompt to select a site before site is chosen', async () => {
    renderList();
    await waitFor(() => expect(getSites).toHaveBeenCalled());
    expect(screen.getByText(/Select a site to view absences/i)).toBeInTheDocument();
  });

  it('renders column headers after selecting a site', async () => {
    const user = userEvent.setup();
    renderList();
    await user.click(screen.getByRole('combobox'));
    const option = await screen.findByRole('option', { name: /Main Office/i });
    await user.click(option);
    await waitFor(() => expect(screen.getByText('Person')).toBeInTheDocument());
    expect(screen.getByText('Type')).toBeInTheDocument();
    expect(screen.getByText('Start')).toBeInTheDocument();
    expect(screen.getByText('End')).toBeInTheDocument();
    expect(screen.getByText('Reason')).toBeInTheDocument();
  });

  it('loads people and absences after site is selected', async () => {
    const user = userEvent.setup();
    renderList();

    await user.click(screen.getByRole('combobox'));
    const option = await screen.findByRole('option', { name: /Main Office/i });
    await user.click(option);

    await waitFor(() => expect(getResources).toHaveBeenCalledWith({ resourceTypeKey: 'person' }));
    await waitFor(() =>
      expect(getResourceAbsences).toHaveBeenCalledWith('person-alice', 'site-1'),
    );
    await waitFor(() => expect(screen.getByText('Alice')).toBeInTheDocument());
    expect(screen.getByText('Vacation')).toBeInTheDocument();
    expect(screen.getByText('Summer break')).toBeInTheDocument();
  });

  it('shows empty message when no absences for selected site', async () => {
    const user = userEvent.setup();
    vi.mocked(getResourceAbsences).mockResolvedValue([]);
    renderList();

    await user.click(screen.getByRole('combobox'));
    const option = await screen.findByRole('option', { name: /Main Office/i });
    await user.click(option);

    await waitFor(() =>
      expect(screen.getByText(/No absences recorded for this site/i)).toBeInTheDocument(),
    );
  });

  it('calls deleteResourceAbsence when delete button is clicked', async () => {
    const user = userEvent.setup();
    renderList();

    await user.click(screen.getByRole('combobox'));
    const option = await screen.findByRole('option', { name: /Main Office/i });
    await user.click(option);

    await waitFor(() => screen.getByText('Alice'));
    await user.click(screen.getByRole('button', { name: /Delete absence for Alice/i }));
    await waitFor(() =>
      expect(deleteResourceAbsence).toHaveBeenCalledWith('person-alice', 'abs-1'),
    );
  });
});
