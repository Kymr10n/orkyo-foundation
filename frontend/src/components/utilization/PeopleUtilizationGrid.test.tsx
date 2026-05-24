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
vi.mock('@foundation/src/lib/api/resource-groups-api', () => ({
  getResourceGroups: vi.fn().mockResolvedValue([]),
  getResourceGroupMembers: vi.fn().mockResolvedValue({ groupId: '', members: [] }),
}));

import { getResources } from '@foundation/src/lib/api/resources-api';
import { getResourceUtilization } from '@foundation/src/lib/api/resource-utilization-api';
import { getPersonProfile } from '@foundation/src/lib/api/person-profiles-api';
import { getResourceGroups, getResourceGroupMembers } from '@foundation/src/lib/api/resource-groups-api';

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

function cellsWithStatus(container: HTMLElement, status: string): HTMLElement[] {
  return Array.from(container.querySelectorAll(`[data-status="${status}"]`)) as HTMLElement[];
}

describe('PeopleUtilizationGrid', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getResources).mockResolvedValue(twoPeople);
    vi.mocked(getResourceUtilization).mockResolvedValue(availableUtil);
    vi.mocked(getPersonProfile).mockResolvedValue(null as never);
    vi.mocked(getResourceGroups).mockResolvedValue([]);
    vi.mocked(getResourceGroupMembers).mockResolvedValue({ groupId: '', members: [] });
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
    const { container } = renderGrid();
    await waitFor(() => screen.getByText('Alice Smith'));
    await waitFor(() =>
      expect(cellsWithStatus(container, 'available').length).toBeGreaterThan(0),
    );
  });

  it('marks overbooked bucket with overbooked status', async () => {
    const overbookedUtil: ResourceUtilizationResponse = {
      ...availableUtil,
      buckets: makeBuckets(31, { allocatedPercent: 120, effectiveAvailabilityPercent: 100 }),
    };
    vi.mocked(getResourceUtilization).mockResolvedValue(overbookedUtil);
    const { container } = renderGrid();
    await waitFor(() => screen.getByText('Alice Smith'));
    await waitFor(() =>
      expect(cellsWithStatus(container, 'overbooked').length).toBeGreaterThan(0),
    );
  });

  it('marks non-working bucket with non-working status', async () => {
    const nonWorkingUtil: ResourceUtilizationResponse = {
      ...availableUtil,
      buckets: makeBuckets(31, { effectiveAvailabilityPercent: 0, allocatedPercent: 0 }),
    };
    vi.mocked(getResourceUtilization).mockResolvedValue(nonWorkingUtil);
    const { container } = renderGrid();
    await waitFor(() => screen.getByText('Alice Smith'));
    await waitFor(() =>
      expect(cellsWithStatus(container, 'non-working').length).toBeGreaterThan(0),
    );
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

  it('uses EEE dd column header format for day granularity (week scale)', async () => {
    renderGrid({ scale: 'week', anchorTs: new Date('2026-05-11T00:00:00Z') });
    await waitFor(() => screen.getByText('Alice Smith'));
    expect(screen.getByText('Mon 11')).toBeInTheDocument();
  });

  it('renders column headers while utilization data is still loading', async () => {
    vi.mocked(getResourceUtilization).mockReturnValue(new Promise(() => {}));

    renderGrid({ scale: 'week', anchorTs: new Date('2026-05-11T00:00:00Z') });

    await waitFor(() => screen.getByText('Alice Smith'));
    expect(screen.getByText('Mon 11')).toBeInTheDocument();
    expect(screen.getByText('Sun 17')).toBeInTheDocument();
    expect(screen.getByText('Person')).toBeInTheDocument();
  });

  it('renders 7 day-columns for week scale', async () => {
    renderGrid({ scale: 'week', anchorTs: new Date('2026-05-11T00:00:00Z') });
    await waitFor(() => screen.getByText('Mon 11'));

    const dayLabels = ['Mon 11', 'Tue 12', 'Wed 13', 'Thu 14', 'Fri 15', 'Sat 16', 'Sun 17'];
    for (const label of dayLabels) {
      expect(screen.getByText(label)).toBeInTheDocument();
    }
  });

  it('renders 24 hour-columns for day scale', async () => {
    renderGrid({ scale: 'day', anchorTs: new Date('2026-05-11T00:00:00Z') });
    await waitFor(() => screen.getByText('Alice Smith'));

    expect(screen.getByText('00:00')).toBeInTheDocument();
    expect(screen.getByText('12:00')).toBeInTheDocument();
    expect(screen.getByText('23:00')).toBeInTheDocument();
  });

  it('renders 5 week-columns for month scale (5-week sliding window)', async () => {
    renderGrid({ scale: 'month', anchorTs: new Date('2026-05-11T00:00:00Z') });
    await waitFor(() => screen.getByText('Alice Smith'));

    // Month scale = 5 weekly buckets starting at the anchor's Monday.
    const weekLabels = ['May 11', 'May 18', 'May 25', 'Jun 01', 'Jun 08'];
    for (const label of weekLabels) {
      expect(screen.getByText(label)).toBeInTheDocument();
    }
  });

  it('renders 12 month-columns for year scale', async () => {
    renderGrid({ scale: 'year', anchorTs: new Date('2026-05-11T00:00:00Z') });
    await waitFor(() => screen.getByText('Alice Smith'));

    // Year scale = 12 monthly buckets starting at the anchor's month, with 2-digit year.
    const monthLabels = ["May '26", "Jun '26", "Jul '26", "Aug '26", "Sep '26", "Oct '26", "Nov '26", "Dec '26", "Jan '27", "Feb '27", "Mar '27", "Apr '27"];
    for (const label of monthLabels) {
      expect(screen.getByText(label)).toBeInTheDocument();
    }
  });

  it('column headers update immediately when anchorTs changes to a new week', async () => {
    const { rerender } = renderGrid({ scale: 'week', anchorTs: new Date('2026-05-11T00:00:00Z') });
    await waitFor(() => screen.getByText('Mon 11'));

    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    });
    rerender(
      <QueryClientProvider client={queryClient}>
        <PeopleUtilizationGrid anchorTs={new Date('2026-05-18T00:00:00Z')} scale="week" />
      </QueryClientProvider>,
    );

    await waitFor(() => expect(screen.getByText('Mon 18')).toBeInTheDocument());
    expect(screen.queryByText('Mon 11')).not.toBeInTheDocument();
  });

  // ── Legend color-consistency tests ──────────────────────────────────────────

  function getLegendDot(label: string): HTMLElement {
    return screen.getByText(label).firstElementChild as HTMLElement;
  }

  it('legend dot for "Available" has the same background class as an available cell', async () => {
    const { container } = renderGrid();
    await waitFor(() => screen.getByText('Available'));

    const dot = getLegendDot('Available');
    expect(dot.className).toMatch(/bg-emerald-100/);
    expect(dot.className).toMatch(/dark:bg-emerald-950/);

    await waitFor(() => expect(cellsWithStatus(container, 'available').length).toBeGreaterThan(0));
    const availableCell = cellsWithStatus(container, 'available')[0];
    expect(availableCell.className).toMatch(/bg-emerald-100/);
    expect(availableCell.className).toMatch(/dark:bg-emerald-950/);
  });

  it('legend dot for "Assigned" has the same background class as an assigned cell', async () => {
    const assignedUtil: ResourceUtilizationResponse = {
      ...availableUtil,
      buckets: makeBuckets(31, { isExclusiveOccupied: true, effectiveAvailabilityPercent: 100 }),
    };
    vi.mocked(getResourceUtilization).mockResolvedValue(assignedUtil);
    const { container } = renderGrid();
    await waitFor(() => screen.getByText('Alice Smith'));

    const dot = getLegendDot('Assigned');
    expect(dot.className).toMatch(/bg-blue-100/);
    expect(dot.className).toMatch(/dark:bg-blue-950/);

    await waitFor(() => expect(cellsWithStatus(container, 'assigned').length).toBeGreaterThan(0));
    const assignedCell = cellsWithStatus(container, 'assigned')[0];
    expect(assignedCell.className).toMatch(/bg-blue-100/);
    expect(assignedCell.className).toMatch(/dark:bg-blue-950/);
  });

  it('legend dot for "Overbooked" has the same background class as an overbooked cell', async () => {
    const overbookedUtil: ResourceUtilizationResponse = {
      ...availableUtil,
      buckets: makeBuckets(31, { allocatedPercent: 120, effectiveAvailabilityPercent: 100 }),
    };
    vi.mocked(getResourceUtilization).mockResolvedValue(overbookedUtil);
    const { container } = renderGrid();
    await waitFor(() => screen.getByText('Alice Smith'));

    const dot = getLegendDot('Overbooked');
    expect(dot.className).toMatch(/bg-red-100/);
    expect(dot.className).toMatch(/dark:bg-red-950/);

    await waitFor(() => expect(cellsWithStatus(container, 'overbooked').length).toBeGreaterThan(0));
    const overbookedCell = cellsWithStatus(container, 'overbooked')[0];
    expect(overbookedCell.className).toMatch(/bg-red-100/);
    expect(overbookedCell.className).toMatch(/dark:bg-red-950/);
  });
});
