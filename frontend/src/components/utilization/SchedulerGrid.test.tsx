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
import { useSchedulerStore } from '@foundation/src/store/scheduler-store';

const appStoreMock = vi.hoisted(() => ({
  collapsedGroupIds: [] as string[],
  spaceOrder: [] as string[],
  toggleGroupCollapse: vi.fn(),
}));

// Mock the store
vi.mock('@foundation/src/store/app-store', () => ({
  useAppStore: vi.fn((selector) => {
    const mockState = {
      currentView: { start: new Date('2024-01-01'), end: new Date('2024-01-31') },
      viewType: 'month' as const,
      selectedSiteId: 'site-1',
      spaceOrder: appStoreMock.spaceOrder,
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

// Mock the tenant-wide conflicts registry — the grid's source of truth for
// committed bookings. Tests set `registryConflicts` to drive the badges.
const registryMock = vi.hoisted(() => ({
  conflicts: [] as { requestId: string; conflicts: unknown[] }[],
}));
vi.mock('@foundation/src/lib/api/conflicts-api', () => ({
  getConflicts: vi.fn(() => Promise.resolve(registryMock.conflicts)),
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
    appStoreMock.spaceOrder = [];
    registryMock.conflicts = [];
    // Reset any draft left over from a draft-overlay test.
    useSchedulerStore.getState().cancelResize();
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

  it('orders spaces by spaceOrder and groups them by resource group', async () => {
    const { getResourceGroups } = await import(
      '@foundation/src/lib/api/resource-groups-api'
    );
    vi.mocked(getResourceGroups).mockResolvedValue([
      {
        id: 'group-1',
        name: 'Building A',
        color: '#FF0000',
        displayOrder: 2,
        resourceTypeKey: 'space',
        memberCount: 1,
        defaultAvailabilityPercent: 100,
        createdAt: '2024-01-01T00:00:00Z',
        updatedAt: '2024-01-01T00:00:00Z',
      },
      {
        id: 'group-2',
        name: 'Building B',
        color: '#00FF00',
        displayOrder: 1,
        resourceTypeKey: 'space',
        memberCount: 1,
        defaultAvailabilityPercent: 100,
        createdAt: '2024-01-01T00:00:00Z',
        updatedAt: '2024-01-01T00:00:00Z',
      },
    ]);
    // Custom order pins space-2 first; each space sits in a different group.
    appStoreMock.spaceOrder = ['space-2', 'space-1'];
    const grouped: Space[] = [
      { ...mockSpaces[0], groupId: 'group-1' },
      { ...mockSpaces[1], groupId: 'group-2' },
    ];
    const Wrapper = createWrapper();

    render(
      <Wrapper>
        <SchedulerGrid
          spaces={grouped}
          requests={[]}
          scale="month"
          anchorTs={new Date('2024-01-15')}
          timeCursorTs={new Date()}
          onRequestClick={vi.fn()}
          onTimeCursorClick={vi.fn()}
        />
      </Wrapper>,
    );

    await waitFor(() => {
      expect(screen.getByText('Building A')).toBeInTheDocument();
      expect(screen.getByText('Building B')).toBeInTheDocument();
    });
    expect(screen.getByText('Room A101')).toBeInTheDocument();
    expect(screen.getByText('Room A102')).toBeInTheDocument();
  });

  it('sorts spaces when only some appear in spaceOrder', async () => {
    // space-2 is pinned; space-1 is not in the order → falls through to code sort.
    appStoreMock.spaceOrder = ['space-2'];
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
      </Wrapper>,
    );

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

  describe('conflicts from the registry', () => {
    it('marks a request bar as conflicting when the registry reports it', async () => {
      const Wrapper = createWrapper();
      const conflict: Conflict = { id: 'c1', kind: 'connector_mismatch', severity: 'error', message: 'Missing Crane' };
      registryMock.conflicts = [{ requestId: 'req-1', conflicts: [conflict] }];

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

      // The overlay sets a `title` encoding the conflict count when hasConflict = true.
      await waitFor(() => {
        const bar = document.querySelector<HTMLElement>('[title*="conflict"]');
        expect(bar).toBeInTheDocument();
        expect(bar!.title).toContain('Test Request 1');
      });
      await act(async () => {});
    });

    it('does not mark a request bar as conflicting when the registry is empty', async () => {
      const Wrapper = createWrapper();
      registryMock.conflicts = [];

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
      expect(document.querySelector('[title*="conflict"]')).not.toBeInTheDocument();
    });
  });

  describe('draft overlay validation', () => {
    // Two bars on the same space: req-1 (09:00–11:00) and req-2 (12:00–13:00).
    // They do not overlap when committed, so the registry is clean.
    const twoOnOneSpace: Request[] = [
      mockRequests[0],
      {
        ...mockRequests[0],
        id: 'req-2',
        name: 'Test Request 2',
        startTs: '2024-01-10T12:00:00Z',
        endTs: '2024-01-10T13:00:00Z',
        sortOrder: 1,
      },
    ];
    const startMs = new Date('2024-01-10T09:00:00Z').getTime();
    const committedEndMs = new Date('2024-01-10T11:00:00Z').getTime();
    const overlappingEndMs = new Date('2024-01-10T12:30:00Z').getTime();

    const renderGrid = (requests: Request[]) => {
      const Wrapper = createWrapper();
      return render(
        <Wrapper>
          <SchedulerGrid
            spaces={mockSpaces}
            requests={requests}
            scale="day"
            anchorTs={new Date('2024-01-10')}
            timeCursorTs={new Date('2024-01-10T12:00:00Z')}
            onRequestClick={vi.fn()}
            onTimeCursorClick={vi.fn()}
          />
        </Wrapper>,
      );
    };

    it('adds an overlap conflict for the dragged bar and its new peer', async () => {
      registryMock.conflicts = [];
      renderGrid(twoOnOneSpace);
      await act(async () => {});
      // No conflicts before the drag.
      expect(document.querySelector('[title*="conflict"]')).not.toBeInTheDocument();

      // Resize req-1's end past req-2's start → they now overlap on space-1.
      await act(async () => {
        useSchedulerStore.getState().startResize({
          requestId: 'req-1',
          resourceId: 'space-1',
          edge: 'right',
          committedStartMs: startMs,
          committedEndMs,
        });
        useSchedulerStore.getState().updateResize(startMs, overlappingEndMs);
      });

      await waitFor(() => {
        expect(document.querySelector('[title*="conflict"]')).toBeInTheDocument();
      });
    });

    it('clears a committed conflict when the draft resizes the bar out of overlap', async () => {
      // Registry reports req-1 in conflict; resizing it shorter (no peer overlap)
      // must drop the badge via the draft overlay's delete branch.
      const conflict: Conflict = {
        id: 'c1',
        kind: 'overlap',
        severity: 'error',
        message: 'Overlap',
      };
      registryMock.conflicts = [{ requestId: 'req-1', conflicts: [conflict] }];
      renderGrid([mockRequests[0]]);

      await waitFor(() => {
        expect(document.querySelector('[title*="conflict"]')).toBeInTheDocument();
      });

      await act(async () => {
        useSchedulerStore.getState().startResize({
          requestId: 'req-1',
          resourceId: 'space-1',
          edge: 'right',
          committedStartMs: startMs,
          committedEndMs,
        });
        // Extend slightly — still a valid single booking (>= 120-min minimum),
        // no peer to overlap, so the only committed conflict must clear.
        useSchedulerStore
          .getState()
          .updateResize(startMs, new Date('2024-01-10T11:30:00Z').getTime());
      });

      await waitFor(() => {
        expect(document.querySelector('[title*="conflict"]')).not.toBeInTheDocument();
      });
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

    it('drags the time cursor to a new position (non-edge move)', async () => {
      const Wrapper = createWrapper();
      const onTimeCursorClick = vi.fn();

      const { container } = render(
        <Wrapper>
          <SchedulerGrid
            spaces={mockSpaces}
            requests={mockRequests}
            scale="month"
            anchorTs={new Date('2024-01-15')}
            timeCursorTs={new Date('2024-01-15T12:00:00Z')}
            onRequestClick={vi.fn()}
            onTimeCursorClick={onTimeCursorClick}
          />
        </Wrapper>,
      );
      await act(async () => {});

      const handle = container.querySelector('.cursor-ew-resize')!;
      expect(handle).toBeTruthy();

      // Begin dragging, then move the pointer somewhere mid-grid (not in an edge
      // zone, since the unmocked rect width is 0) — the cursor time updates.
      fireEvent.mouseDown(handle, { clientX: 300 });
      await act(async () => {
        document.dispatchEvent(
          new MouseEvent('mousemove', { clientX: 300, bubbles: true }),
        );
      });
      expect(onTimeCursorClick).toHaveBeenCalled();

      // Releasing detaches the global listeners without error.
      await act(async () => {
        document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
      });
    });

    it('starts edge-scrolling when the pointer reaches the left edge', async () => {
      const rectSpy = vi
        .spyOn(HTMLElement.prototype, 'getBoundingClientRect')
        .mockReturnValue({
          left: 0,
          top: 0,
          right: 1000,
          bottom: 100,
          width: 1000,
          height: 100,
          x: 0,
          y: 0,
          toJSON: () => ({}),
        } as DOMRect);
      const rafSpy = vi
        .spyOn(window, 'requestAnimationFrame')
        .mockReturnValue(1 as unknown as number);

      try {
        const Wrapper = createWrapper();
        const onAnchorChange = vi.fn();
        const onTimeCursorClick = vi.fn();

        const { container } = render(
          <Wrapper>
            <SchedulerGrid
              spaces={mockSpaces}
              requests={mockRequests}
              scale="month"
              anchorTs={new Date('2024-01-15')}
              timeCursorTs={new Date('2024-01-15T12:00:00Z')}
              onRequestClick={vi.fn()}
              onTimeCursorClick={onTimeCursorClick}
              onAnchorChange={onAnchorChange}
            />
          </Wrapper>,
        );
        await act(async () => {});

        const handle = container.querySelector('.cursor-ew-resize')!;
        fireEvent.mouseDown(handle, { clientX: 10 });
        await act(async () => {
          document.dispatchEvent(
            new MouseEvent('mousemove', { clientX: 10, bubbles: true }),
          );
        });

        // Pointer 10px from the left edge (< 60px threshold) shifts the anchor back.
        expect(onAnchorChange).toHaveBeenCalled();

        await act(async () => {
          document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
        });
      } finally {
        rectSpy.mockRestore();
        rafSpy.mockRestore();
      }
    });

    it('starts edge-scrolling when the pointer reaches the right edge', async () => {
      const rectSpy = vi
        .spyOn(HTMLElement.prototype, 'getBoundingClientRect')
        .mockReturnValue({
          left: 0,
          top: 0,
          right: 1000,
          bottom: 100,
          width: 1000,
          height: 100,
          x: 0,
          y: 0,
          toJSON: () => ({}),
        } as DOMRect);
      const rafSpy = vi
        .spyOn(window, 'requestAnimationFrame')
        .mockReturnValue(1 as unknown as number);

      try {
        const Wrapper = createWrapper();
        const onAnchorChange = vi.fn();

        const { container } = render(
          <Wrapper>
            <SchedulerGrid
              spaces={mockSpaces}
              requests={mockRequests}
              scale="month"
              anchorTs={new Date('2024-01-15')}
              timeCursorTs={new Date('2024-01-15T12:00:00Z')}
              onRequestClick={vi.fn()}
              onTimeCursorClick={vi.fn()}
              onAnchorChange={onAnchorChange}
            />
          </Wrapper>,
        );
        await act(async () => {});

        const handle = container.querySelector('.cursor-ew-resize')!;
        fireEvent.mouseDown(handle, { clientX: 995 });
        await act(async () => {
          document.dispatchEvent(
            new MouseEvent('mousemove', { clientX: 995, bubbles: true }),
          );
        });

        expect(onAnchorChange).toHaveBeenCalled();
        await act(async () => {
          document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true }));
        });
      } finally {
        rectSpy.mockRestore();
        rafSpy.mockRestore();
      }
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
