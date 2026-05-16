import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { PeopleUtilizationGrid } from './PeopleUtilizationGrid';
import type { ResourcesResponse } from '@foundation/src/lib/api/resources-api';
import type { ResourceUtilizationResponse } from '@foundation/src/lib/api/resource-utilization-api';

vi.mock('@foundation/src/lib/api/resources-api', () => ({
  getResources: vi.fn(),
}));
vi.mock('@foundation/src/lib/api/resource-utilization-api', () => ({
  getResourceUtilization: vi.fn(),
}));

import { getResources } from '@foundation/src/lib/api/resources-api';
import { getResourceUtilization } from '@foundation/src/lib/api/resource-utilization-api';

const ANCHOR = new Date('2026-05-01T00:00:00Z');

const emptyPeople: ResourcesResponse = { data: [], total: 0, page: 1, pageSize: 50 };

const twoPeople: ResourcesResponse = {
  data: [
    {
      id: 'p-alice',
      resourceTypeId: 'rt-person',
      resourceTypeKey: 'person',
      name: 'Alice',
      allocationMode: 'Exclusive',
      baseAvailabilityPercent: 100,
      isActive: true,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    },
    {
      id: 'p-bob',
      resourceTypeId: 'rt-person',
      resourceTypeKey: 'person',
      name: 'Bob',
      allocationMode: 'Fractional',
      baseAvailabilityPercent: 80,
      isActive: true,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    },
  ],
  total: 2,
  page: 1,
  pageSize: 50,
};

function makeBuckets(
  count: number,
  overrides: Partial<{
    allocatedPercent: number;
    effectiveAvailabilityPercent: number;
    isExclusiveOccupied: boolean;
  }> = {},
): ResourceUtilizationResponse['buckets'] {
  return Array.from({ length: count }, (_, i) => ({
    start: new Date(ANCHOR.getTime() + i * 86400_000).toISOString(),
    end: new Date(ANCHOR.getTime() + (i + 1) * 86400_000).toISOString(),
    allocatedPercent: 0,
    effectiveAvailabilityPercent: 100,
    isExclusiveOccupied: false,
    ...overrides,
  }));
}

const availableUtil: ResourceUtilizationResponse = {
  from: ANCHOR.toISOString(),
  to: new Date(ANCHOR.getFullYear(), ANCHOR.getMonth() + 1, 1).toISOString(),
  granularity: 'day',
  buckets: makeBuckets(31),
};

function renderGrid(props?: Partial<React.ComponentProps<typeof PeopleUtilizationGrid>>) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <PeopleUtilizationGrid anchorTs={ANCHOR} scale="month" {...props} />
    </QueryClientProvider>,
  );
}

describe('PeopleUtilizationGrid', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getResources).mockResolvedValue(twoPeople);
    vi.mocked(getResourceUtilization).mockResolvedValue(availableUtil);
  });

  it('renders nothing while people are loading', () => {
    vi.mocked(getResources).mockReturnValue(new Promise(() => {}));
    const { container } = renderGrid();
    expect(container.firstChild).toBeNull();
  });

  it('renders nothing when no people exist', async () => {
    vi.mocked(getResources).mockResolvedValue(emptyPeople);
    const { container } = renderGrid();
    await waitFor(() => expect(getResources).toHaveBeenCalled());
    expect(container.firstChild).toBeNull();
  });

  it('renders the section header with people count', async () => {
    renderGrid();
    await waitFor(() => expect(screen.getByTestId('people-utilization-grid')).toBeInTheDocument());
    expect(screen.getByText(/People \(2\)/)).toBeInTheDocument();
  });

  it('renders a row for each person', async () => {
    renderGrid();
    await waitFor(() => {
      expect(screen.getByText('Alice')).toBeInTheDocument();
      expect(screen.getByText('Bob')).toBeInTheDocument();
    });
  });

  it('collapses and hides the grid body when toggle is clicked', async () => {
    renderGrid();
    await waitFor(() => screen.getByTestId('people-grid-toggle'));

    expect(screen.getByTestId('people-grid-body')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('people-grid-toggle'));
    expect(screen.queryByTestId('people-grid-body')).not.toBeInTheDocument();
  });

  it('expands again after a second toggle click', async () => {
    renderGrid();
    await waitFor(() => screen.getByTestId('people-grid-toggle'));

    const toggle = screen.getByTestId('people-grid-toggle');
    fireEvent.click(toggle); // collapse
    fireEvent.click(toggle); // expand
    expect(screen.getByTestId('people-grid-body')).toBeInTheDocument();
  });

  it('calls getResources with person resourceTypeKey and isActive=true', async () => {
    renderGrid();
    await waitFor(() => expect(getResources).toHaveBeenCalled());
    expect(getResources).toHaveBeenCalledWith({ resourceTypeKey: 'person', isActive: true });
  });

  it('calls getResourceUtilization for each person', async () => {
    renderGrid();
    await waitFor(() =>
      expect(getResourceUtilization).toHaveBeenCalledTimes(2),
    );
    expect(getResourceUtilization).toHaveBeenCalledWith(
      'p-alice',
      expect.any(Date),
      expect.any(Date),
      expect.any(String),
    );
  });

  it('renders bucket cells with correct status for available buckets', async () => {
    renderGrid();
    await waitFor(() => screen.getByText('Alice'));
    const cells = screen.getAllByRole('cell', { hidden: true }).filter(
      (c) => c.getAttribute('data-status') === 'available',
    );
    expect(cells.length).toBeGreaterThan(0);
  });

  it('marks overbooked bucket with overbooked status', async () => {
    const overbookedUtil: ResourceUtilizationResponse = {
      ...availableUtil,
      buckets: makeBuckets(31, { allocatedPercent: 120, effectiveAvailabilityPercent: 100 }),
    };
    vi.mocked(getResourceUtilization).mockResolvedValue(overbookedUtil);
    renderGrid();
    await waitFor(() => screen.getByText('Alice'));
    const cells = screen.getAllByRole('cell', { hidden: true }).filter(
      (c) => c.getAttribute('data-status') === 'overbooked',
    );
    expect(cells.length).toBeGreaterThan(0);
  });

  it('marks non-working bucket with non-working status', async () => {
    const nonWorkingUtil: ResourceUtilizationResponse = {
      ...availableUtil,
      buckets: makeBuckets(31, { effectiveAvailabilityPercent: 0, allocatedPercent: 0 }),
    };
    vi.mocked(getResourceUtilization).mockResolvedValue(nonWorkingUtil);
    renderGrid();
    await waitFor(() => screen.getByText('Alice'));
    const cells = screen.getAllByRole('cell', { hidden: true }).filter(
      (c) => c.getAttribute('data-status') === 'non-working',
    );
    expect(cells.length).toBeGreaterThan(0);
  });
});
