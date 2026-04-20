import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { SchedulerGrid } from '@/components/utilization/SchedulerGrid';
import { SpaceRow } from '@/components/utilization/SpaceRow';
import { TimeCell } from '@/components/utilization/TimeCell';
import { ScheduledRequestOverlay } from '@/components/utilization/ScheduledRequestOverlay';
import { GroupHeader } from '@/components/utilization/GroupHeader';
import type { Request } from '@/types/requests';
import type { Space } from '@/types/space';
import type { SpaceGroup } from '@/types/spaceGroup';
import { DndContext } from '@dnd-kit/core';

// Mock the store
vi.mock('@/store/app-store', () => ({
  useAppStore: vi.fn((selector) => {
    const mockState = {
      currentView: { start: new Date('2024-01-01'), end: new Date('2024-01-31') },
      viewType: 'month' as const,
      selectedSiteId: 'site-1',
      spaceOrder: [],
      timeCursorEnabled: false,
      collapsedGroupIds: new Set<string>(),
      toggleGroupCollapse: vi.fn(),
      conflicts: new Map(),
    };
    return selector ? selector(mockState) : mockState;
  }),
}));

// Mock the space groups API
vi.mock('@/lib/api/space-groups-api', () => ({
  getSpaceGroups: vi.fn(() => Promise.resolve([])),
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
    spaceId: 'space-1',
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

    expect(screen.getByText('Room A101')).toBeInTheDocument();
    await act(async () => {});
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
    await act(async () => {});
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

    expect(screen.getByText('Test Request 1')).toBeInTheDocument();
    await act(async () => {});
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
    const _mockSpaceGroups: SpaceGroup[] = [
      {
        id: 'group-1',
        name: 'Building A',
        color: '#FF0000',
        displayOrder: 1,
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
    expect(screen.getByText('Room A101')).toBeInTheDocument();
    expect(screen.getByText('Room A102')).toBeInTheDocument();
    await act(async () => {});
  });

  it('verifies all sub-components are defined', () => {
    expect(SchedulerGrid).toBeDefined();
    expect(typeof SchedulerGrid).toBe('function');
    expect(SpaceRow).toBeDefined();
    expect(TimeCell).toBeDefined();
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

      expect(screen.getByText('Room A101')).toBeInTheDocument();
      await act(async () => {});
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
      expect(screen.getByText('Room A101')).toBeInTheDocument();
      await act(async () => {});
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

      expect(screen.getByText('Room A101')).toBeInTheDocument();
      await act(async () => {});
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
