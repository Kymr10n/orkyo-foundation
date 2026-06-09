import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { PeopleUtilizationGrid } from './PeopleUtilizationGrid';
import { useAppStore } from '@foundation/src/store/app-store';
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
vi.mock('@foundation/src/lib/api/person-candidate-requests-api', () => ({
  getPersonAssignmentOptions: vi.fn().mockResolvedValue([]),
  mismatchCount: vi.fn().mockReturnValue(0),
  matchesAllRequirements: vi.fn().mockReturnValue(true),
}));
vi.mock('@foundation/src/lib/api/resource-assignments-api', () => ({
  getAssignmentsByResource: vi.fn().mockResolvedValue([]),
  validateAssignmentsBatch: vi.fn().mockResolvedValue([]),
}));

import { getResources } from '@foundation/src/lib/api/resources-api';
import { getResourceUtilization } from '@foundation/src/lib/api/resource-utilization-api';
import { getPersonProfile } from '@foundation/src/lib/api/person-profiles-api';
import { getResourceGroups, getResourceGroupMembers } from '@foundation/src/lib/api/resource-groups-api';
import { getAssignmentsByResource, validateAssignmentsBatch } from '@foundation/src/lib/api/resource-assignments-api';
import type { ResourceAssignmentInfo } from '@foundation/src/lib/api/resource-assignments-api';

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
    useAppStore.setState({ collapsedGroupIds: [] });
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

  it('uses a people-scoped collapse id so space groups do not collapse people', async () => {
    useAppStore.setState({ collapsedGroupIds: ['spaces:ungrouped'] });
    renderGrid();

    await waitFor(() => expect(screen.getByText('Alice Smith')).toBeInTheDocument());

    fireEvent.click(screen.getByText('Ungrouped'));

    expect(useAppStore.getState().collapsedGroupIds).toContain('spaces:ungrouped');
    expect(useAppStore.getState().collapsedGroupIds).toContain('people:ungrouped');
    expect(useAppStore.getState().collapsedGroupIds).not.toContain('ungrouped');
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

  it('renders segment bars after utilization data loads', async () => {
    renderGrid();
    await waitFor(() => screen.getByText('Alice Smith'));
    await waitFor(() =>
      expect(screen.getAllByTestId('person-segment-bar').length).toBeGreaterThan(0),
    );
  });

  it('shows overall utilization % in the label cell', async () => {
    const partialUtil: ResourceUtilizationResponse = {
      ...availableUtil,
      buckets: makeBuckets(31, { allocatedPercent: 60, effectiveAvailabilityPercent: 100 }),
    };
    vi.mocked(getResourceUtilization).mockResolvedValue(partialUtil);
    renderGrid();
    await waitFor(() => screen.getByText('Alice Smith'));
    // overallPercent averages the working-hour buckets → 60%
    await waitFor(() => expect(screen.getAllByText('60%').length).toBeGreaterThan(0));
  });

  it('clicking a segment opens the person assignment dialog', async () => {
    renderGrid({ scale: 'week', anchorTs: new Date('2026-05-11T00:00:00Z') });
    await waitFor(() => screen.getByText('Alice Smith'));
    // Wait for at least one segment bar to appear
    await waitFor(() => expect(screen.getAllByTestId('person-segment-bar').length).toBeGreaterThan(0));
    const bar = screen.getAllByTestId('person-segment-bar')[0];
    await userEvent.click(bar);
    await waitFor(() =>
      expect(screen.getByTestId('person-assignment-dialog')).toBeInTheDocument(),
    );
    // Dialog title names the person
    expect(screen.getByText(/Assignments — Alice Smith/)).toBeInTheDocument();
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
    // getAllByText because segment bars may also render the same status text
    expect(screen.getAllByText('Available').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Booked').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Assigned').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Overbooked').length).toBeGreaterThan(0);
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

  it('renders 4 quarter-hour columns for hour scale', async () => {
    renderGrid({ scale: 'hour', anchorTs: new Date(2026, 4, 11, 10, 20, 0) });
    await waitFor(() => screen.getByText('Alice Smith'));

    expect(screen.getByText('10:15')).toBeInTheDocument();
    expect(screen.getByText('10:30')).toBeInTheDocument();
    expect(screen.getByText('10:45')).toBeInTheDocument();
    expect(screen.getByText('11:00')).toBeInTheDocument();
    expect(getResourceUtilization).toHaveBeenCalledWith(
      'p-alice',
      expect.any(Date),
      expect.any(Date),
      'minute',
    );
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
  // Segment bar color correctness is covered in PersonSegmentBar.test.tsx.
  // Here we only verify the legend dots use the expected Tailwind color classes.

  // LegendDot renders <span class="flex items-center gap-1"><span class="inline-block ..."/>{label}</span>.
  // The dot is the first-child span. Segment bars may also render the same label text, so we
  // locate the legend span by its unique gap-1 + items-center structure (first child = dot).
  function getLegendDot(label: string): HTMLElement {
    const candidates = screen.getAllByText(label);
    const legendSpan = candidates.find(
      (el) => el.tagName === 'SPAN' && el.firstElementChild?.tagName === 'SPAN',
    );
    return legendSpan!.firstElementChild as HTMLElement;
  }

  it('legend dot for "Available" uses the emerald palette', async () => {
    renderGrid();
    await waitFor(() => expect(screen.getAllByText('Available').length).toBeGreaterThan(0));
    const dot = getLegendDot('Available');
    expect(dot.className).toMatch(/bg-emerald-100/);
    expect(dot.className).toMatch(/dark:bg-emerald-950/);
  });

  it('legend dot for "Assigned" uses the blue palette', async () => {
    renderGrid();
    await waitFor(() => screen.getByText('Assigned'));
    const dot = getLegendDot('Assigned');
    expect(dot.className).toMatch(/bg-blue-100/);
    expect(dot.className).toMatch(/dark:bg-blue-950/);
  });

  it('legend dot for "Overbooked" uses the red palette', async () => {
    renderGrid();
    await waitFor(() => screen.getByText('Overbooked'));
    const dot = getLegendDot('Overbooked');
    expect(dot.className).toMatch(/bg-red-100/);
    expect(dot.className).toMatch(/dark:bg-red-950/);
  });

  // ── Conflict-check deferral tests ───────────────────────────────────────────

  const oneAssignment: ResourceAssignmentInfo = {
    id: 'asgn-1',
    requestId: 'req-1',
    resourceId: 'p-alice',
    resourceTypeKey: 'person',
    startUtc: '2026-05-01T08:00:00Z',
    endUtc: '2026-05-01T17:00:00Z',
    assignmentStatus: 'active',
    createdAt: '2026-05-01T00:00:00Z',
    updatedAt: '2026-05-01T00:00:00Z',
  };

  it('does not call validateAssignmentsBatch immediately when assignments load', async () => {
    // The conflict-check query is deferred by CONFLICT_CHECK_DELAY_MS so the grid
    // renders immediately. In a fast test the timer never fires, confirming the call
    // is not part of the initial render path.
    vi.mocked(getAssignmentsByResource).mockResolvedValue([oneAssignment]);
    renderGrid();
    await waitFor(() => screen.getByText('Alice Smith'));
    expect(validateAssignmentsBatch).not.toHaveBeenCalled();
  });
});
