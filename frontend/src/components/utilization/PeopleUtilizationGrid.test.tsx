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
vi.mock('@foundation/src/lib/api/person-profiles-api', () => ({
  getPersonProfile: vi.fn().mockResolvedValue(null),
}));

import { getResources } from '@foundation/src/lib/api/resources-api';
import { getResourceUtilization } from '@foundation/src/lib/api/resource-utilization-api';
import { getPersonProfile } from '@foundation/src/lib/api/person-profiles-api';

const ANCHOR = new Date('2026-05-01T00:00:00Z');

const emptyPeople: ResourcesResponse = { data: [], total: 0, page: 1, pageSize: 50 };

const twoPeople: ResourcesResponse = {
  data: [
    {
      id: 'p-alice',
      resourceTypeId: 'rt-person',
      resourceTypeKey: 'person',
      name: 'Alice Smith',
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
      name: 'Bob Jones',
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
    vi.mocked(getPersonProfile).mockResolvedValue(null as never);
  });

  it('shows loading state while people are loading', () => {
    vi.mocked(getResources).mockReturnValue(new Promise(() => {}));
    renderGrid();
    expect(screen.getByText(/loading people/i)).toBeInTheDocument();
  });

  it('shows empty state when no people exist', async () => {
    vi.mocked(getResources).mockResolvedValue(emptyPeople);
    renderGrid();
    await waitFor(() => expect(screen.getByText(/no people defined/i)).toBeInTheDocument());
  });

  it('renders the grid container', async () => {
    renderGrid();
    await waitFor(() => expect(screen.getByTestId('people-utilization-grid')).toBeInTheDocument());
  });

  it('renders a row for each person', async () => {
    renderGrid();
    await waitFor(() => {
      expect(screen.getByText('Alice Smith')).toBeInTheDocument();
      expect(screen.getByText('Bob Jones')).toBeInTheDocument();
    });
  });

  it('renders initials in the left column', async () => {
    renderGrid();
    await waitFor(() => screen.getByText('Alice Smith'));
    expect(screen.getByText('AS')).toBeInTheDocument(); // Alice Smith → AS
    expect(screen.getByText('BJ')).toBeInTheDocument(); // Bob Jones → BJ
  });

  it('shows job title from profile when available', async () => {
    vi.mocked(getPersonProfile).mockResolvedValue({
      resourceId: 'p-alice',
      jobTitleName: 'Senior Engineer',
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    });
    renderGrid();
    await waitFor(() => screen.getByText('Alice Smith'));
    await waitFor(() => expect(screen.getAllByText('Senior Engineer').length).toBeGreaterThan(0));
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
    await waitFor(() => screen.getByText('Alice Smith'));
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
    await waitFor(() => screen.getByText('Alice Smith'));
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
    await waitFor(() => screen.getByText('Alice Smith'));
    const cells = screen.getAllByRole('cell', { hidden: true }).filter(
      (c) => c.getAttribute('data-status') === 'non-working',
    );
    expect(cells.length).toBeGreaterThan(0);
  });

  it('shows percentage label inside allocated cells', async () => {
    const partialUtil: ResourceUtilizationResponse = {
      ...availableUtil,
      buckets: makeBuckets(31, { allocatedPercent: 60, effectiveAvailabilityPercent: 100 }),
    };
    vi.mocked(getResourceUtilization).mockResolvedValue(partialUtil);
    renderGrid();
    await waitFor(() => screen.getByText('Alice Smith'));
    const labels = screen.getAllByText('60%');
    expect(labels.length).toBeGreaterThan(0);
  });

  it('filters people by search input', async () => {
    renderGrid();
    await waitFor(() => {
      expect(screen.getByText('Alice Smith')).toBeInTheDocument();
      expect(screen.getByText('Bob Jones')).toBeInTheDocument();
    });

    fireEvent.change(screen.getByPlaceholderText(/search people/i), {
      target: { value: 'Alice' },
    });

    expect(screen.getByText('Alice Smith')).toBeInTheDocument();
    expect(screen.queryByText('Bob Jones')).not.toBeInTheDocument();
  });

  it('shows no-match message when search yields no results', async () => {
    renderGrid();
    await waitFor(() => screen.getByText('Alice Smith'));

    fireEvent.change(screen.getByPlaceholderText(/search people/i), {
      target: { value: 'xyz-no-match' },
    });

    expect(screen.getByText(/no people match/i)).toBeInTheDocument();
  });

  it('renders the legend strip', async () => {
    renderGrid();
    await waitFor(() => screen.getByTestId('people-utilization-grid'));
    expect(screen.getByText('Available')).toBeInTheDocument();
    expect(screen.getByText('Partial')).toBeInTheDocument();
    expect(screen.getByText('Assigned')).toBeInTheDocument();
    expect(screen.getByText('Overbooked')).toBeInTheDocument();
  });

  it('uses EEE d column header format for day granularity (week scale)', async () => {
    renderGrid({ scale: 'week', anchorTs: new Date('2026-05-11T00:00:00Z') });
    await waitFor(() => screen.getByText('Alice Smith'));
    // 'Mon 11' format (EEE d)
    expect(screen.getByText('Mon 11')).toBeInTheDocument();
  });

  // ── Legend color-consistency tests ──────────────────────────────────────────
  // These verify that legend dots use the same color classes as the cells they
  // describe. A mismatch here means dark/light mode renders the wrong colors.

  // getLegendDot: LegendDot renders <span class="flex items-center gap-1"><span class="...colors..." />{label}</span>
  // getByText returns the outer wrapper span; the color dot is its first child element.
  function getLegendDot(label: string): HTMLElement {
    return screen.getByText(label).firstElementChild as HTMLElement;
  }

  it('legend dot for "Available" has the same background class as an available cell', async () => {
    renderGrid();
    await waitFor(() => screen.getByText('Available'));

    const dot = getLegendDot('Available');
    expect(dot.className).toMatch(/bg-emerald-100/);
    expect(dot.className).toMatch(/dark:bg-emerald-950/);

    const availableCell = screen.getAllByRole('cell', { hidden: true }).find(
      (c) => c.getAttribute('data-status') === 'available',
    );
    expect(availableCell?.className).toMatch(/bg-emerald-100/);
    expect(availableCell?.className).toMatch(/dark:bg-emerald-950/);
  });

  it('legend dot for "Assigned" has the same background class as an assigned cell', async () => {
    const assignedUtil: ResourceUtilizationResponse = {
      ...availableUtil,
      buckets: makeBuckets(31, { isExclusiveOccupied: true, effectiveAvailabilityPercent: 100 }),
    };
    vi.mocked(getResourceUtilization).mockResolvedValue(assignedUtil);
    renderGrid();
    await waitFor(() => screen.getByText('Alice Smith'));

    const dot = getLegendDot('Assigned');
    expect(dot.className).toMatch(/bg-blue-100/);
    expect(dot.className).toMatch(/dark:bg-blue-950/);

    const assignedCell = screen.getAllByRole('cell', { hidden: true }).find(
      (c) => c.getAttribute('data-status') === 'assigned',
    );
    expect(assignedCell?.className).toMatch(/bg-blue-100/);
    expect(assignedCell?.className).toMatch(/dark:bg-blue-950/);
  });

  it('legend dot for "Overbooked" has the same background class as an overbooked cell', async () => {
    const overbookedUtil: ResourceUtilizationResponse = {
      ...availableUtil,
      buckets: makeBuckets(31, { allocatedPercent: 120, effectiveAvailabilityPercent: 100 }),
    };
    vi.mocked(getResourceUtilization).mockResolvedValue(overbookedUtil);
    renderGrid();
    await waitFor(() => screen.getByText('Alice Smith'));

    const dot = getLegendDot('Overbooked');
    expect(dot.className).toMatch(/bg-red-100/);
    expect(dot.className).toMatch(/dark:bg-red-950/);

    const overbookedCell = screen.getAllByRole('cell', { hidden: true }).find(
      (c) => c.getAttribute('data-status') === 'overbooked',
    );
    expect(overbookedCell?.className).toMatch(/bg-red-100/);
    expect(overbookedCell?.className).toMatch(/dark:bg-red-950/);
  });
});
