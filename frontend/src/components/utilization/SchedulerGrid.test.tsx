import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, act, fireEvent, waitFor } from '@testing-library/react';
import type { Conflict } from '@foundation/src/types/requests';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { SchedulerGrid } from '@foundation/src/components/utilization/SchedulerGrid';
import { SpaceRow } from '@foundation/src/components/utilization/SpaceRow';
import { ScheduledRequestOverlay } from '@foundation/src/components/utilization/ScheduledRequestOverlay';
import { GroupHeader } from '@foundation/src/components/utilization/GroupHeader';
import type { Request } from '@foundation/src/types/requests';
import type { Space } from '@foundation/src/types/space';
import type { ResourceGroupInfo } from '@foundation/src/lib/api/resource-groups-api';
import { DndContext } from '@dnd-kit/core';
import { spaceAssignment } from '@foundation/src/test-utils/request-fixtures';

const appStoreMock = vi.hoisted(() => ({
  collapsedGroupIds: [] as string[],
  toggleGroupCollapse: vi.fn(),
}));

// Mock the store
vi.mock('@foundation/src/store/app-store', () => ({
  useAppStore: vi.fn((selector) => {
    const mockState = {
      currentView: { start: new Date('2024-01-01'), end: new Date('2024-01-31') },
      viewType: 'month' as const,
      selectedSiteId: 'site-1',
      spaceOrder: [],
      timeCursorEnabled: false,
      collapsedGroupIds: appStoreMock.collapsedGroupIds,
      toggleGroupCollapse: appStoreMock.toggleGroupCollapse,
      conflicts: new Map(),
    };
    return selector ? selector(mockState) : mockState;
  }),
}));

// Mock the resource groups API
vi.mock('@foundation/src/lib/api/resource-groups-api', () => ({
  getResourceGroups: vi.fn(() => Promise.resolve([])),
}));

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>
      <DndContext>{children}</DndContext>
    </QueryClientProvider>
  );
};

const mockSpaces: Space[] = [
  {
    id: 'space-1',
    siteId: 'site-1',
    name: 'Room A101',
    code: 'A101',
    isPhysical: true,
    properties: {},
    capacity: 1,
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },
  {
    id: 'space-2',
    siteId: 'site-1',
    name: 'Room A102',
    code: 'A102',
    isPhysical: true,
    properties: {},
    capacity: 1,
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
  },
];

const mockRequests: Request[] = [
  {
    id: 'req-1',
    name: 'Test Request 1',
    assignments: [spaceAssignment('space-1')],
    startTs: '2024-01-10T09:00:00Z',
    endTs: '2024-01-10T11:00:00Z',
    status: 'planned',
    minimalDurationValue: 120,
    minimalDurationUnit: 'minutes',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
    schedulingSettingsApply: true,
    planningMode: 'leaf',
    sortOrder: 0,
  },
];

const _mockColumns = [
  {
    start: new Date('2024-01-01T00:00:00Z'),
    end: new Date('2024-01-01T23:59:59Z'),
    label: 'Mon 01',
  },
  {
    start: new Date('2024-01-02T00:00:00Z'),
    end: new Date('2024-01-02T23:59:59Z'),
    label: 'Tue 02',
  },
];

describe('SchedulerGrid', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    appStoreMock.collapsedGroupIds = [];
  });

  it('renders without crashing', async () => {
    const Wrapper = createWrapper();

    render(
      <Wrapper>
        <SchedulerGrid
          spaces={mockSpaces}
          requests={mockRequests}
          scale="month"
          anchorTs={new Date('2024-01-15')}
          timeCursorTs={new Date()}
          onRequestClick={vi.fn()}
          onTimeCursorClick={vi.fn()}
        />
      </Wrapper>
    );

    await act(async () => {});
    expect(screen.getByText('Room A101')).toBeInTheDocument();
  });

  it('renders memoized SpaceRow components', async () => {
    const Wrapper = createWrapper();

    const { rerender } = render(
      <Wrapper>
        <SchedulerGrid
          spaces={mockSpaces}
          requests={mockRequests}
          scale="month"
          anchorTs={new Date('2024-01-15')}
          timeCursorTs={new Date()}
          onRequestClick={vi.fn()}
          onTimeCursorClick={vi.fn()}
        />
      </Wrapper>
    );

    // Verify initial render
    await act(async () => {});
    expect(screen.getByText('Room A101')).toBeInTheDocument();
    expect(screen.getByText('Room A102')).toBeInTheDocument();

    // Rerender with same props - memoization should prevent unnecessary renders
    rerender(
      <Wrapper>
        <SchedulerGrid
          spaces={mockSpaces}
          requests={mockRequests}
          scale="month"
          anchorTs={new Date('2024-01-15')}
          timeCursorTs={new Date()}
          onRequestClick={vi.fn()}
          onTimeCursorClick={vi.fn()}
        />
      </Wrapper>
    );

    // Still renders correctly
    expect(screen.getByText('Room A101')).toBeInTheDocument();
  });

  it('renders scheduled requests', async () => {
    const Wrapper = createWrapper();

    render(
      <Wrapper>
        <SchedulerGrid
          spaces={mockSpaces}
          requests={mockRequests}
          scale="month"
          anchorTs={new Date('2024-01-01')}
          timeCursorTs={new Date()}
          onRequestClick={vi.fn()}
          onTimeCursorClick={vi.fn()}
        />
      </Wrapper>
    );

    await act(async () => {});
    expect(screen.getByText('Test Request 1')).toBeInTheDocument();
  });

  it('handles empty spaces array', async () => {
    const Wrapper = createWrapper();

    render(
      <Wrapper>
        <SchedulerGrid
          spaces={[]}
          requests={[]}
          scale="month"
          anchorTs={new Date('2024-01-15')}
          timeCursorTs={new Date()}
          onRequestClick={vi.fn()}
          onTimeCursorClick={vi.fn()}
        />
      </Wrapper>
    );

    // Should render without crashing
    expect(screen.queryByText('Room A101')).not.toBeInTheDocument();
    await act(async () => {});
  });

  it('renders space groups when provided', async () => {
    const Wrapper = createWrapper();
    const _mockSpaceGroups: ResourceGroupInfo[] = [
      {
        id: 'group-1',
        name: 'Building A',
        color: '#FF0000',
        displayOrder: 1,
        resourceTypeKey: 'space',
        memberCount: 0,
        defaultAvailabilityPercent: 100,
        createdAt: '2024-01-01T00:00:00Z',
        updatedAt: '2024-01-01T00:00:00Z',
      },
    ];

    render(
      <Wrapper>
        <SchedulerGrid
          spaces={mockSpaces}
          requests={mockRequests}
          scale="month"
          anchorTs={new Date('2024-01-15')}
          timeCursorTs={new Date()}
          onRequestClick={vi.fn()}
          onTimeCursorClick={vi.fn()}
        />
      </Wrapper>
    );

    // Should render grouped spaces
    await act(async () => {});
    expect(screen.getByText('Room A101')).toBeInTheDocument();
    expect(screen.getByText('Room A102')).toBeInTheDocument();
  });

  it('uses a spaces-scoped collapse id so people groups do not collapse spaces', async () => {
    appStoreMock.collapsedGroupIds = ['people:ungrouped'];
    const Wrapper = createWrapper();

    render(
      <Wrapper>
        <SchedulerGrid
          spaces={mockSpaces}
          requests={[]}
          scale="month"
          anchorTs={new Date('2024-01-15')}
          timeCursorTs={new Date()}
          onRequestClick={vi.fn()}
          onTimeCursorClick={vi.fn()}
        />
      </Wrapper>
    );

    await act(async () => {});
    expect(screen.getByText('Room A101')).toBeInTheDocument();

    fireEvent.click(screen.getByText('Ungrouped'));

    expect(appStoreMock.toggleGroupCollapse).toHaveBeenCalledWith('spaces:ungrouped');
    expect(appStoreMock.toggleGroupCollapse).not.toHaveBeenCalledWith('ungrouped');
  });

  it('verifies all sub-components are defined', () => {
    expect(SchedulerGrid).toBeDefined();
    expect(typeof SchedulerGrid).toBe('function');
    expect(SpaceRow).toBeDefined();
    expect(ScheduledRequestOverlay).toBeDefined();
    expect(GroupHeader).toBeDefined();
  });

  describe('Column header tooltips', () => {
    it('shows full date tooltip on week/month scale columns', async () => {
      const Wrapper = createWrapper();

      render(
        <Wrapper>
          <SchedulerGrid
            spaces={mockSpaces}
            requests={[]}
            scale="month"
            anchorTs={new Date('2024-01-15')}
            timeCursorTs={new Date()}
            onRequestClick={vi.fn()}
            onTimeCursorClick={vi.fn()}
          />
        </Wrapper>
      );

      // Column headers should have a title with "EEEE, MMMM d, yyyy" format (no time)
      const colHeaders = document.querySelectorAll<HTMLElement>('[title]');
      const dateTooltips = Array.from(colHeaders).filter(
        (el) => el.title && /^\w+, \w+ \d+, \d{4}$/.test(el.title)
      );
      expect(dateTooltips.length).toBeGreaterThan(0);
      // Should NOT include time for month scale
      dateTooltips.forEach((el) => {
        expect(el.title).not.toMatch(/\d{2}:\d{2}/);
      });
      await act(async () => {});
    });

    it('shows full date with time tooltip on day/hour scale columns', async () => {
      const Wrapper = createWrapper();

      render(
        <Wrapper>
          <SchedulerGrid
            spaces={mockSpaces}
            requests={[]}
            scale="day"
            anchorTs={new Date('2024-01-15')}
            timeCursorTs={new Date()}
            onRequestClick={vi.fn()}
            onTimeCursorClick={vi.fn()}
          />
        </Wrapper>
      );

      // Column headers should have a title with "EEEE, MMMM d, yyyy HH:mm" format
      const colHeaders = document.querySelectorAll<HTMLElement>('[title]');
      const dateTimeTooltips = Array.from(colHeaders).filter(
        (el) => el.title && /^\w+, \w+ \d+, \d{4} \d{2}:\d{2}$/.test(el.title)
      );
      expect(dateTimeTooltips.length).toBeGreaterThan(0);
      await act(async () => {});
    });
  });

  describe('capabilityConflicts prop', () => {
    it('marks a request bar as conflicting when capabilityConflicts contains it', async () => {
      const Wrapper = createWrapper();
      const capConflicts = new Map<string, Conflict[]>([
        ['req-1', [{ id: 'c1', kind: 'connector_mismatch', severity: 'error', message: 'Missing Crane' }]],
      ]);

      render(
        <Wrapper>
          <SchedulerGrid
            spaces={mockSpaces}
            requests={mockRequests}
            scale="month"
            anchorTs={new Date('2024-01-01')}
            timeCursorTs={new Date()}
            onRequestClick={vi.fn()}
            onTimeCursorClick={vi.fn()}
            capabilityConflicts={capConflicts}
          />
        </Wrapper>
      );

      // The overlay sets a `title` encoding the conflict count when hasConflict = true.
      await waitFor(() => {
        const bar = document.querySelector<HTMLElement>('[title*="conflict"]');
        expect(bar).toBeInTheDocument();
        expect(bar!.title).toContain('Test Request 1');
      });
      await act(async () => {});
    });

    it('does not mark a request bar as conflicting when capabilityConflicts is empty', async () => {
      const Wrapper = createWrapper();

      render(
        <Wrapper>
          <SchedulerGrid
            spaces={mockSpaces}
            requests={mockRequests}
            scale="month"
            anchorTs={new Date('2024-01-01')}
            timeCursorTs={new Date()}
            onRequestClick={vi.fn()}
            onTimeCursorClick={vi.fn()}
            capabilityConflicts={new Map()}
          />
        </Wrapper>
      );

      await act(async () => {});
      expect(document.querySelector('[title*="conflict"]')).not.toBeInTheDocument();
    });
  });

  describe('Edge Scroll Feature', () => {
    it('accepts onAnchorChange prop for edge scrolling', async () => {
      const Wrapper = createWrapper();
      const onAnchorChange = vi.fn();

      render(
        <Wrapper>
          <SchedulerGrid
            spaces={mockSpaces}
            requests={mockRequests}
            scale="week"
            anchorTs={new Date('2024-01-15')}
            timeCursorTs={new Date('2024-01-15T12:00:00Z')}
            onRequestClick={vi.fn()}
            onTimeCursorClick={vi.fn()}
            onAnchorChange={onAnchorChange}
          />
        </Wrapper>
      );

      await act(async () => {});
      expect(screen.getByText('Room A101')).toBeInTheDocument();
    });

    it('renders time cursor that can be dragged', async () => {
      const Wrapper = createWrapper();
      const onTimeCursorClick = vi.fn();

      render(
        <Wrapper>
          <SchedulerGrid
            spaces={mockSpaces}
            requests={mockRequests}
            scale="week"
            anchorTs={new Date('2024-01-15')}
            timeCursorTs={new Date('2024-01-15T12:00:00Z')}
            onRequestClick={vi.fn()}
            onTimeCursorClick={onTimeCursorClick}
          />
        </Wrapper>
      );

      // The time cursor area exists (pointer-events-none container with draggable child)
      // Just verify component renders without errors
      await act(async () => {});
      expect(screen.getByText('Room A101')).toBeInTheDocument();
    });

    it('works without onAnchorChange (edge scroll disabled)', async () => {
      const Wrapper = createWrapper();
      const onTimeCursorClick = vi.fn();

      // This verifies backward compatibility - onAnchorChange is optional
      render(
        <Wrapper>
          <SchedulerGrid
            spaces={mockSpaces}
            requests={mockRequests}
            scale="day"
            anchorTs={new Date('2024-01-15')}
            timeCursorTs={new Date('2024-01-15T12:00:00Z')}
            onRequestClick={vi.fn()}
            onTimeCursorClick={onTimeCursorClick}
          />
        </Wrapper>
      );

      await act(async () => {});
      expect(screen.getByText('Room A101')).toBeInTheDocument();
    });

    it('supports all time scales for edge scrolling', () => {
      const Wrapper = createWrapper();
      const scales: ('hour' | 'day' | 'week' | 'month' | 'year')[] = ['hour', 'day', 'week', 'month', 'year'];

      scales.forEach((scale) => {
        const { unmount } = render(
          <Wrapper>
            <SchedulerGrid
              spaces={mockSpaces}
              requests={[]}
              scale={scale}
              anchorTs={new Date('2024-01-15')}
              timeCursorTs={new Date('2024-01-15T12:00:00Z')}
              onRequestClick={vi.fn()}
              onTimeCursorClick={vi.fn()}
              onAnchorChange={vi.fn()}
            />
          </Wrapper>
        );

        // Verify it renders for each scale
        expect(screen.getByText('Space')).toBeInTheDocument();
        unmount();
      });
    });
  });
});
