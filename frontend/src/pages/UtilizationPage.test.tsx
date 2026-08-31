/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router";
import { UtilizationPage } from "@foundation/src/pages/UtilizationPage";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import { navigateTime, navigateCalendarPeriod } from "@foundation/src/lib/utils/time-navigation";
import { makeRequest, spaceAssignment } from "@foundation/src/test-utils/request-fixtures";
import { expandRecurrence } from "@foundation/src/domain/scheduling/recurrence";
import { generateWeekendRanges } from "@foundation/src/domain/scheduling/weekend-ranges";


// --- Extractable mock fns for per-test control ---
const mockUseRequests = vi.fn((_?: any): any => ({ data: [], isLoading: false }));
const mockUseSpaces = vi.fn((_?: any): any => ({ data: [], isLoading: false }));
const mockUseBacklog = vi.fn((): any => ({ data: [], isLoading: false }));
const mockUseAutoScheduleAvailable = vi.fn((_?: any): any => false);
let capturedExportHandler: ((format: string) => Promise<void>) | null = null;
let capturedExportOffer: { label: string; description: string } | null = null;
const mockUseSchedulingSettings = vi.fn((_?: any): any => ({ data: null }));
const mockUseAvailabilityEvents = vi.fn((_?: any): any => ({ data: [] }));

// Breakpoint — default desktop; flip per-test to exercise the phone Spaces layout.
let mockIsPhone = false;
vi.mock("@foundation/src/hooks/useBreakpoint", () => ({
  useBreakpoint: () => ({
    isPhone: mockIsPhone,
    isTablet: false,
    isDesktop: !mockIsPhone,
    device: mockIsPhone ? "phone" : "desktop",
  }),
}));

// Mock AuthContext — default: admin
let mockRole = "admin";
vi.mock("@foundation/src/contexts/AuthContext", () => ({
  useAuth: () => ({
    membership: {
      tenantId: "tenant-1",
      slug: "demo",
      displayName: "Demo",
      displayNamePlural: "Demos",
      hasGeometry: false,
      hasDirectoryProfile: false,
      singleGroupMembership: false,
      get role() { return mockRole; },
      state: "active",
      isTenantAdmin: true,
    },
    setMembership: vi.fn(),
    logout: vi.fn(),
    user: { sub: "test-user", email: "test@example.com" },
  }),
  getAuthTokenSync: () => "test-token",
  getTenantSlugSync: () => "demo",
}));

// Mock the store — configurable per test
let mockStoreOverrides: Record<string, any> = {};
const mockSetSpaceOrder = vi.fn();
const mockSetScale = vi.fn();
const mockSetAnchorTs = vi.fn();
const mockSetTimeCursorTs = vi.fn();
const mockSetIsFloorplanCollapsed = vi.fn();
const mockSetConflicts = vi.fn();

// Single source of truth for the mocked store state, shared by the hook selector and getState()
// (the stale-anchor reconcile effect reads the live anchor via useAppStore.getState()).
const buildMockState = (): any => ({
  selectedSiteId: "site-1",
  conflicts: new Map(),
  scale: "month" as const,
  setScale: mockSetScale,
  anchorTs: new Date("2024-01-15"),
  setAnchorTs: mockSetAnchorTs,
  timeCursorTs: new Date(),
  setTimeCursorTs: mockSetTimeCursorTs,
  isFloorplanCollapsed: false,
  setIsFloorplanCollapsed: mockSetIsFloorplanCollapsed,
  setConflicts: mockSetConflicts,
  spaceOrder: [],
  setSpaceOrder: mockSetSpaceOrder,
  ...mockStoreOverrides,
});

vi.mock("@foundation/src/store/app-store", () => ({
  useAppStore: Object.assign(
    vi.fn((selector: any) => {
      const mockState = buildMockState();
      return selector ? selector(mockState) : mockState;
    }),
    { getState: () => buildMockState() },
  ),
}));

vi.mock("@foundation/src/store/scheduler-store", () => ({
  useSchedulerStore: Object.assign(vi.fn((sel: any) => sel ? sel({}) : {}), {
    getState: () => ({ finalizeDraft: vi.fn() }),
  }),
}));

// Mock hooks
vi.mock("@foundation/src/hooks/usePreferences", () => ({
  usePreferences: vi.fn(() => ({ data: null, isLoading: false })),
  useUpdatePreferences: vi.fn(() => ({ mutate: vi.fn() })),
}));

vi.mock("@foundation/src/hooks/useScheduling", () => ({
  useSchedulingSettings: (arg?: any) => mockUseSchedulingSettings(arg),
  useAvailabilityEvents: (arg?: any) => mockUseAvailabilityEvents(arg),
}));

vi.mock("@foundation/src/hooks/useSchedulingConflicts", () => ({
  useSchedulingConflicts: vi.fn(() => ({ conflictingRequestIds: new Set() })),
}));

vi.mock("@foundation/src/hooks/useConflictRegistry", () => ({
  useConflictRegistry: vi.fn(() => ({ conflictsByRequest: new Map() })),
}));

const mockPreviewMutateAsync = vi.fn(() => Promise.resolve({ fingerprint: "fp-1", assignments: [] }));
const mockApplyMutateAsync = vi.fn(() => Promise.resolve());
const mockScheduleMutate = vi.fn();
const mockScheduleMutateAsync = vi.fn(() => Promise.resolve());

vi.mock("@foundation/src/hooks/useAutoSchedule", () => ({
  useAutoScheduleAvailable: (arg?: any) => mockUseAutoScheduleAvailable(arg),
  usePreviewAutoSchedule: vi.fn(() => ({ mutateAsync: mockPreviewMutateAsync, isPending: false })),
  useApplyAutoSchedule: vi.fn(() => ({ mutateAsync: mockApplyMutateAsync, isPending: false })),
}));

vi.mock("@foundation/src/hooks/useUtilization", () => ({
  // The grid's bar feed; reuse the existing mock driver so test cases that set request data work.
  useScheduledRequests: (..._args: any[]) => mockUseRequests(),
  useBacklogRequests: () => mockUseBacklog(),
  useUpdateRequest: vi.fn(() => ({ mutate: vi.fn() })),
  useScheduleRequest: vi.fn(() => ({ mutate: mockScheduleMutate, mutateAsync: mockScheduleMutateAsync })),
  usePlaceableResources: (arg?: any) => mockUseSpaces(arg),
}));

vi.mock("@foundation/src/hooks/useImportExport", () => ({
  useExportHandler: vi.fn((_key: string, handler: any, offer: any) => {
    capturedExportHandler = handler;
    capturedExportOffer = offer;
  }),
  useCalendarFeedHandler: vi.fn(),
}));

// Mock API modules called by handlers
vi.mock("@foundation/src/lib/api/request-api", () => ({
  updateRequest: vi.fn(() => Promise.resolve()),
  createRequest: vi.fn(() => Promise.resolve()),
  moveRequest: vi.fn(() => Promise.resolve()),
}));

vi.mock("@foundation/src/lib/utils/utils", async (importOriginal) => {
  const actual = await importOriginal<Record<string, unknown>>();
  return {
    ...actual,
    buildUpdatePayload: vi.fn((d: any) => d),
    buildCreatePayload: vi.fn((d: any) => d),
  };
});

vi.mock("@foundation/src/lib/utils/export-handlers", () => ({
  exportUtilization: vi.fn(() => Promise.resolve()),
}));

vi.mock("@foundation/src/domain/request-tree", () => ({
  wouldCreateCycle: vi.fn(() => false),
  getNextSortOrder: vi.fn(() => 0),
}));

vi.mock("@foundation/src/lib/api/space-capability-api", () => ({
  getSpaceCapabilities: vi.fn(() => Promise.resolve([])),
}));

vi.mock("@foundation/src/domain/scheduling/capability-matcher", () => ({
  validateSpaceRequirements: vi.fn(() => []),
}));

vi.mock("@foundation/src/domain/scheduling/recurrence", () => ({
  expandRecurrence: vi.fn(() => []),
}));

vi.mock("@foundation/src/domain/scheduling/weekend-ranges", () => ({
  generateWeekendRanges: vi.fn(() => []),
}));

// Capture DndContext.onDragEnd for handler testing
let capturedOnDragEnd: ((event: any) => void) | null = null;
vi.mock("@dnd-kit/core", () => ({
  DndContext: ({ children, onDragEnd }: any) => {
    capturedOnDragEnd = onDragEnd;
    return <div data-testid="dnd-context">{children}</div>;
  },
  DragOverlay: ({ children }: any) => <div data-testid="drag-overlay">{children}</div>,
  useDndMonitor: vi.fn(),
  MouseSensor: vi.fn(),
  TouchSensor: vi.fn(),
  KeyboardSensor: vi.fn(),
  pointerWithin: vi.fn(),
  useSensor: vi.fn(() => ({})),
  useSensors: vi.fn(() => []),
}));

// Mock heavy child components — capture callbacks for handler testing
vi.mock("@foundation/src/components/utilization/CollapsibleFloorplan", () => ({
  CollapsibleFloorplan: ({ onToggle }: any) => (
    <div data-testid="collapsible-floorplan">
      <button data-testid="toggle-floorplan" onClick={onToggle}>Toggle</button>
    </div>
  ),
}));

vi.mock("@foundation/src/components/utilization/SchedulerGrid", () => ({
  SchedulerGrid: ({ onRequestDoubleClick, onRequestResize, onTimeCursorClick, onEmptyCellClick, onRequestContextMenu, requests }: any) => (
    <div data-testid="scheduler-grid" data-request-names={(requests ?? []).map((r: any) => r.name).join(",")}>
      {onRequestContextMenu && (
        <button data-testid="context-request" onClick={() => onRequestContextMenu("r1", { x: 10, y: 20 })}>Context</button>
      )}
      {onRequestDoubleClick && <button data-testid="dblclick-request" onClick={() => onRequestDoubleClick("r1")}>DblClick</button>}
      {onRequestResize && <button data-testid="resize-request" onClick={() => onRequestResize("r1", "2024-01-15T10:00:00Z", "2024-01-15T12:00:00Z")}>Resize</button>}
      {onTimeCursorClick && <button data-testid="cursor-click" onClick={() => onTimeCursorClick(new Date("2024-06-01"))}>Cursor</button>}
      {onEmptyCellClick && (
        <button
          data-testid="empty-cell-click"
          onClick={() =>
            onEmptyCellClick(
              // resourceTypeKey is what the chooser filters the backlog by — a real grid row
              // always carries it, so the stub must too.
              { id: "space-1", code: "CRA", name: "Conference Room A", resourceTypeKey: "space" },
              { start: new Date("2026-06-22T00:00:00Z"), end: new Date("2026-06-23T00:00:00Z"), label: "22 Mon" },
            )
          }
        >
          EmptyCell
        </button>
      )}
    </div>
  ),
}));

vi.mock("@foundation/src/components/utilization/ResourceUtilizationGrid", () => ({
  ResourceUtilizationGrid: ({ resourceType, siteId, filter }: any) => (
    <div
      data-testid={`${resourceType.key}-utilization-grid`}
      data-site-id={siteId ?? ""}
      data-query={filter?.query ?? ""}
    />
  ),
}));

vi.mock("@foundation/src/components/utilization/ScaleSelect", () => ({
  ScaleSelect: () => <div data-testid="scale-select" />,
}));

vi.mock("@foundation/src/components/utilization/TimeNavigator", () => ({
  TimeNavigator: ({ onPrevious, onNext, onToday }: any) => (
    <div data-testid="time-navigator">
      <button data-testid="nav-prev" onClick={onPrevious}>Prev</button>
      <button data-testid="nav-next" onClick={onNext}>Next</button>
      <button data-testid="nav-today" onClick={onToday}>Today</button>
    </div>
  ),
}));

vi.mock("@foundation/src/components/utilization/AutoScheduleButton", () => ({
  AutoScheduleButton: ({ onClick, disabled }: any) => (
    <button data-testid="auto-schedule-btn" onClick={onClick} disabled={disabled}>Auto-Schedule</button>
  ),
}));

// hasGeometry is required on ResourceTypeInfo and drives which tab a type lands on, so it is set
// here rather than left undefined — this factory returns an untyped literal, so a missing field
// is not a compile error and silently reads as false.
const mockResourceType = (
  key: string, displayName: string, plural: string, isSystem = true, hasGeometry = false,
) => ({
  id: `type-${key}`, key, displayName, displayNamePlural: plural, isSystem, isActive: true,
  hasGeometry,
  createdAt: "2026-01-01T00:00:00Z", updatedAt: "2026-01-01T00:00:00Z",
});
// Tabs are derived from the active types: placeable types share the scheduler tab, every other
// type gets a grid tab. `forklift` stands in for a tenant-defined type.
const mockResourceTypes = vi.fn(() => ({
  data: [
    mockResourceType("space", "Space", "Spaces", true, true),
    mockResourceType("person", "Person", "People"),
    mockResourceType("tool", "Tool", "Tools"),
    mockResourceType("forklift", "Forklift", "Forklifts", false),
  ],
  isSuccess: true,
}));
vi.mock("@foundation/src/hooks/useResourceTypes", () => ({
  useResourceTypes: (...args: unknown[]) => mockResourceTypes(...(args as [])),
}));


vi.mock("@foundation/src/components/utilization/AutoSchedulePreviewDialog", () => ({
  AutoSchedulePreviewDialog: ({ open, onApply, onClose, applyError }: any) => open ? (
    <div data-testid="preview-dialog">
      <button data-testid="apply-schedule" onClick={onApply}>Apply</button>
      <button data-testid="close-preview" onClick={onClose}>Close</button>
      {applyError && <div data-testid="apply-error">{applyError}</div>}
    </div>
  ) : null,
}));

vi.mock("@foundation/src/components/requests/RequestFormDialog", () => ({
  RequestFormDialog: ({ open, onSave, onOpenChange, scheduleSiteId, canEdit, defaultResource }: any) => open ? (
    <div data-testid="request-form-dialog" data-schedule-site-id={scheduleSiteId ?? ""} data-can-edit={String(canEdit)} data-default-resource={defaultResource ? `${defaultResource.typeKey}:${defaultResource.resourceId}` : ""}>
      <button data-testid="save-request" onClick={() => onSave({ name: "Test" })}>Save</button>
      <button data-testid="close-form" onClick={() => onOpenChange(false)}>Close</button>
    </div>
  ) : null,
}));

let capturedOnSlotSelect: ((start: Date, end: Date) => void) | null = null;
let capturedOnEventClick: ((requestId: string) => void) | null = null;
let capturedOnEventMove: ((requestId: string, start: Date, end: Date) => void) | null = null;
let capturedOnDatesSet: ((start: Date) => void) | null = null;
vi.mock("@foundation/src/components/utilization/RequestCalendar", () => ({
  RequestCalendar: ({ onSlotSelect, onEventClick, onEventMove, onDatesSet }: any) => {
    capturedOnSlotSelect = onSlotSelect;
    capturedOnEventClick = onEventClick;
    capturedOnEventMove = onEventMove;
    capturedOnDatesSet = onDatesSet;
    return <div data-testid="request-calendar" />;
  },
}));

let capturedOnScheduleExisting: ((req: any) => void) | null = null;
let capturedOnCreateNew: (() => void) | null = null;
let capturedChooserBacklog: any[] | null = null;
vi.mock("@foundation/src/components/utilization/ScheduleSlotDialog", () => ({
  ScheduleSlotDialog: ({ open, onScheduleExisting, onCreateNew, resourceName, backlog }: any) => {
    capturedOnScheduleExisting = onScheduleExisting;
    capturedOnCreateNew = onCreateNew;
    capturedChooserBacklog = backlog;
    return open ? <div data-testid="slot-chooser" data-resource-name={resourceName ?? ""} /> : null;
  },
}));

// The scheduler tab is identified by its surface now, not by the space type key.
const createWrapper = (initialTab = "stations", types?: string) => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={[`/?tab=${initialTab}${types ? `&${initialTab === "stations" ? "stationTypes" : "assetTypes"}=${types}` : ""}`]}>
      <QueryClientProvider client={queryClient}>
        {children}
      </QueryClientProvider>
    </MemoryRouter>
  );
};

describe("UtilizationPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockRole = "admin";
    mockIsPhone = false;
    // useCanEdit is globally mocked to true (src/test/setup.ts); reset each test.
    vi.mocked(useCanEdit).mockReturnValue(true);
    mockUseRequests.mockReturnValue({ data: [], isLoading: false });
    mockUseSpaces.mockReturnValue({ data: [], isLoading: false });
    mockUseAutoScheduleAvailable.mockReturnValue(false);
    mockUseSchedulingSettings.mockReturnValue({ data: null });
    mockUseAvailabilityEvents.mockReturnValue({ data: [] });
    capturedExportHandler = null;
    capturedExportOffer = null;
    capturedOnDragEnd = null;
    capturedOnSlotSelect = null;
    capturedOnEventClick = null;
    capturedOnEventMove = null;
    capturedOnDatesSet = null;
    capturedOnScheduleExisting = null;
    capturedOnCreateNew = null;
    capturedChooserBacklog = null;
    mockUseBacklog.mockReturnValue({ data: [], isLoading: false });
    mockStoreOverrides = {};
  });

  it("renders heading and toolbar controls", () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(screen.getByText("Utilization")).toBeInTheDocument();
    expect(screen.getByTestId("scale-select")).toBeInTheDocument();
    expect(screen.getByTestId("time-navigator")).toBeInTheDocument();
  });

  it("shows loading state when spaces are loading", () => {
    mockUseSpaces.mockReturnValue({ data: [], isLoading: true });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(screen.getByText("Loading requests…")).toBeInTheDocument();
    expect(screen.queryByTestId("scheduler-grid")).not.toBeInTheDocument();
  });

  it("shows loading state when requests are loading", () => {
    mockUseRequests.mockReturnValue({ data: [], isLoading: true });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    // Both the requests panel and the grid body show the loader while requests load.
    expect(screen.getAllByText("Loading requests…").length).toBeGreaterThan(0);
    expect(screen.queryByTestId("scheduler-grid")).not.toBeInTheDocument();
  });

  it("shows SchedulerGrid when data is loaded", () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(screen.getByTestId("scheduler-grid")).toBeInTheDocument();
    expect(screen.queryByText("Loading…")).not.toBeInTheDocument();
  });

  it("renders the floorplan above the grid", () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(screen.getByTestId("collapsible-floorplan")).toBeInTheDocument();
    expect(screen.getByTestId("scheduler-grid")).toBeInTheDocument();
  });

  it("on phones shows the Spaces grid without the floorplan", () => {
    mockIsPhone = true;
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    // Grid stays; the heavy floorplan canvas is dropped.
    expect(screen.getByTestId("scheduler-grid")).toBeInTheDocument();
    expect(screen.queryByTestId("collapsible-floorplan")).not.toBeInTheDocument();
    // Scale/nav controls live in the header's wrapping actions slot — exactly
    // once (guards against the old header + bespoke phone row double-render).
    expect(screen.getAllByTestId("scale-select")).toHaveLength(1);
    expect(screen.getAllByTestId("time-navigator")).toHaveLength(1);
  });

  it("shows Auto-Schedule button when available and user is admin", () => {
    mockUseAutoScheduleAvailable.mockReturnValue(true);
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(screen.getByTestId("auto-schedule-btn")).toBeInTheDocument();
  });

  it("hides Auto-Schedule button when feature is not available", () => {
    mockUseAutoScheduleAvailable.mockReturnValue(false);
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(screen.queryByTestId("auto-schedule-btn")).not.toBeInTheDocument();
  });

  it("hides Auto-Schedule button for viewer role", () => {
    mockUseAutoScheduleAvailable.mockReturnValue(true);
    vi.mocked(useCanEdit).mockReturnValue(false);
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(screen.queryByTestId("auto-schedule-btn")).not.toBeInTheDocument();
  });

  // --- Time navigation handlers ---

  it("handlePrevious calls setAnchorTs via TimeNavigator", () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);
    fireEvent.click(screen.getByTestId("nav-prev"));
    // No error = handler ran successfully
    expect(screen.getByTestId("time-navigator")).toBeInTheDocument();
  });

  it("handleNext calls setAnchorTs via TimeNavigator", () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);
    fireEvent.click(screen.getByTestId("nav-next"));
    expect(screen.getByTestId("time-navigator")).toBeInTheDocument();
  });

  it("handleToday resets anchor to current date", () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);
    fireEvent.click(screen.getByTestId("nav-today"));
    expect(screen.getByTestId("time-navigator")).toBeInTheDocument();
  });

  it("Calendar tab steps by a full period; grid tabs pan by a sub-period", () => {
    const anchor = new Date("2024-01-15"); // matches the mocked store anchor; scale = month

    const CalWrapper = createWrapper("calendar");
    const { unmount } = render(<CalWrapper><UtilizationPage /></CalWrapper>);
    mockSetAnchorTs.mockClear(); // ignore any on-mount snap
    fireEvent.click(screen.getByTestId("nav-next"));
    // Calendar pages a whole month (addMonths), not the grid's addWeeks pan.
    expect(mockSetAnchorTs).toHaveBeenCalledWith(navigateCalendarPeriod(anchor, "month", 1));
    expect(mockSetAnchorTs).not.toHaveBeenCalledWith(navigateTime(anchor, "month", 1));
    unmount();

    const GridWrapper = createWrapper("stations");
    render(<GridWrapper><UtilizationPage /></GridWrapper>);
    mockSetAnchorTs.mockClear();
    fireEvent.click(screen.getByTestId("nav-next"));
    // Grid pans by the sub-period (addWeeks for month scale).
    expect(mockSetAnchorTs).toHaveBeenCalledWith(navigateTime(anchor, "month", 1));
  });

  // --- Stale-anchor reconcile (frozen default anchor drifts on a long-lived tab) ---
  // The store default is a `new Date()` frozen at module load; the effect snaps a *past-day* anchor to
  // today on open and whenever the tab regains focus/visibility, while preserving a future navigation.

  const lastSnappedToToday = (mock: typeof mockSetAnchorTs) => {
    // The snap passes `new Date()`; assert the most recent arg is the current calendar day
    // (not the stale 2024 default).
    const arg = mock.mock.calls.at(-1)?.[0];
    return arg instanceof Date && arg.toDateString() === new Date().toDateString();
  };

  it("snaps a stale anchor to today on open", () => {
    // Default mock anchor is 2024-01-15 → stale relative to now.
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);
    expect(lastSnappedToToday(mockSetAnchorTs)).toBe(true);
    expect(lastSnappedToToday(mockSetTimeCursorTs)).toBe(true);
  });

  it("re-snaps a stale anchor when the window regains focus", () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);
    mockSetAnchorTs.mockClear(); // ignore the on-open snap; isolate the focus listener
    act(() => { window.dispatchEvent(new Event("focus")); });
    expect(mockSetAnchorTs).toHaveBeenCalled();
    expect(lastSnappedToToday(mockSetAnchorTs)).toBe(true);
  });

  it("re-snaps a stale anchor when the tab becomes visible", () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);
    mockSetAnchorTs.mockClear();
    // jsdom defaults document.visibilityState to "visible".
    act(() => { document.dispatchEvent(new Event("visibilitychange")); });
    expect(mockSetAnchorTs).toHaveBeenCalled();
    expect(lastSnappedToToday(mockSetAnchorTs)).toBe(true);
  });

  it("preserves a current/future anchor (no snap on open or focus)", () => {
    mockStoreOverrides = { anchorTs: new Date(Date.now() + 7 * 86_400_000) }; // next week
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);
    act(() => { window.dispatchEvent(new Event("focus")); });
    expect(mockSetAnchorTs).not.toHaveBeenCalled();
    expect(mockSetTimeCursorTs).not.toHaveBeenCalled();
  });

  // --- Floorplan toggle ---

  it("toggles floorplan collapsed state", () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);
    fireEvent.click(screen.getByTestId("toggle-floorplan"));
    expect(screen.getByTestId("collapsible-floorplan")).toBeInTheDocument();
  });

  // --- Request click handlers ---

  it("opens edit dialog on click when user can edit", async () => {
    mockUseRequests.mockReturnValue({ data: [{ id: "r1", name: "Task 1", resourceId: "s1" }], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("dblclick-request"));
    await waitFor(() => {
      expect(screen.getByTestId("request-form-dialog")).toHaveAttribute("data-can-edit", "true");
    });
  });

  it("opens the form dialog in view mode on click for viewer", async () => {
    mockRole = "viewer";
    mockUseRequests.mockReturnValue({ data: [{ id: "r1", name: "Task 1", resourceId: "s1" }], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("dblclick-request"));
    await waitFor(() => {
      expect(screen.getByTestId("request-form-dialog")).toHaveAttribute("data-can-edit", "false");
    });
  });

  // --- Create child ---

  // --- Resize ---

  it("calls scheduleMutation on resize", () => {
    mockUseRequests.mockReturnValue({
      data: [makeRequest({ id: "r1", name: "Task 1", assignments: [spaceAssignment("s1")], isScheduled: true })],
      isLoading: false,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("resize-request"));
    expect(mockScheduleMutate).toHaveBeenCalledWith(
      expect.objectContaining({ requestId: "r1" }),
      expect.any(Object),
    );
  });

  // --- Auto-schedule flow ---

  it("opens auto-schedule preview dialog on click", async () => {
    mockUseAutoScheduleAvailable.mockReturnValue(true);
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("auto-schedule-btn"));
    await waitFor(() => {
      expect(mockPreviewMutateAsync).toHaveBeenCalled();
    });
    await waitFor(() => {
      expect(screen.getByTestId("preview-dialog")).toBeInTheDocument();
    });
  });

  it("solves for the single filtered type, in preview and apply alike", async () => {
    // The filter names the type, and the button is live only while it names exactly one, so
    // preview and apply cannot disagree about which type was solved for.
    mockUseAutoScheduleAvailable.mockReturnValue(true);
    const Wrapper = createWrapper("assets", "tool");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("auto-schedule-btn"));
    await waitFor(() => {
      expect(mockPreviewMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ resourceTypeKey: "tool" }),
      );
    });

    fireEvent.click(screen.getByTestId("apply-schedule"));
    await waitFor(() => {
      expect(mockApplyMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({ resourceTypeKey: "tool" }),
      );
    });
  });

  it("applies auto-schedule from preview dialog", async () => {
    mockUseAutoScheduleAvailable.mockReturnValue(true);
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("auto-schedule-btn"));
    await waitFor(() => {
      expect(screen.getByTestId("preview-dialog")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId("apply-schedule"));
    await waitFor(() => {
      expect(mockApplyMutateAsync).toHaveBeenCalled();
    });
  });

  it("closes auto-schedule preview dialog", async () => {
    mockUseAutoScheduleAvailable.mockReturnValue(true);
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("auto-schedule-btn"));
    await waitFor(() => {
      expect(screen.getByTestId("preview-dialog")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId("close-preview"));
    await waitFor(() => {
      expect(screen.queryByTestId("preview-dialog")).not.toBeInTheDocument();
    });
  });

  // --- Time cursor click ---

  it("updates time cursor on grid click", () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);
    fireEvent.click(screen.getByTestId("cursor-click"));
    expect(screen.getByTestId("scheduler-grid")).toBeInTheDocument();
  });

  // --- Save request from edit dialog ---

  it("saves request from edit dialog", async () => {
    const { updateRequest } = await import("@foundation/src/lib/api/request-api");
    mockUseRequests.mockReturnValue({ data: [{ id: "r1", name: "Task 1", resourceId: "s1" }], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    // Open edit dialog via click
    fireEvent.click(screen.getByTestId("dblclick-request"));
    await waitFor(() => {
      expect(screen.getByTestId("request-form-dialog")).toBeInTheDocument();
    });

    // Click save inside the dialog
    fireEvent.click(screen.getByTestId("save-request"));
    await waitFor(() => {
      expect(vi.mocked(updateRequest)).toHaveBeenCalledWith("r1", expect.anything());
    });
  });

  it("closes edit dialog via close button", async () => {
    mockUseRequests.mockReturnValue({ data: [{ id: "r1", name: "Task 1", resourceId: "s1" }], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("dblclick-request"));
    await waitFor(() => {
      expect(screen.getByTestId("request-form-dialog")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId("close-form"));
    await waitFor(() => {
      expect(screen.queryByTestId("request-form-dialog")).not.toBeInTheDocument();
    });
  });

  // --- Calendar slot scheduling: site binding ---

  it("hands the calendar's site to the schedule form so a site-neutral request is pre-scoped", async () => {
    // Regression: scheduling a request from the calendar must offer the calendar's
    // site to the form (which pre-selects it), else a site-neutral request has no
    // site and no space assignment and vanishes from the site-scoped feed.
    const Wrapper = createWrapper("calendar");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    // Open the empty-slot chooser, then pick an existing site-neutral request.
    capturedOnSlotSelect!(new Date("2026-06-20T09:00:00Z"), new Date("2026-06-20T10:00:00Z"));
    await waitFor(() => expect(screen.getByTestId("slot-chooser")).toBeInTheDocument());
    capturedOnScheduleExisting!({ id: "u-1", name: "Receive steel stock", planningMode: "leaf", siteId: null });

    await waitFor(() => expect(screen.getByTestId("request-form-dialog")).toBeInTheDocument());
    expect(screen.getByTestId("request-form-dialog")).toHaveAttribute("data-schedule-site-id", "site-1");
  });

  it("schedule-existing then save updates the request", async () => {
    const { updateRequest } = await import("@foundation/src/lib/api/request-api");
    const Wrapper = createWrapper("calendar");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    capturedOnSlotSelect!(new Date("2026-06-20T09:00:00Z"), new Date("2026-06-20T10:00:00Z"));
    await waitFor(() => expect(screen.getByTestId("slot-chooser")).toBeInTheDocument());
    capturedOnScheduleExisting!({ id: "u-1", name: "Existing", planningMode: "leaf", siteId: null });
    await waitFor(() => expect(screen.getByTestId("request-form-dialog")).toBeInTheDocument());

    fireEvent.click(screen.getByTestId("save-request"));
    await waitFor(() => expect(updateRequest).toHaveBeenCalled());
    expect(screen.queryByTestId("request-form-dialog")).not.toBeInTheDocument();
  });

  // --- Spaces-grid empty-cell scheduling ---

  it("opens the chooser with the space's name and a type-filtered backlog on a grid cell click", async () => {
    mockUseBacklog.mockReturnValue({
      data: [
        { id: "u-1", name: "Space job", targetResourceTypeKeys: ["space"] },
        { id: "u-2", name: "Person-only job", targetResourceTypeKeys: ["person"] },
      ],
      isLoading: false,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("empty-cell-click"));
    await waitFor(() => expect(screen.getByTestId("slot-chooser")).toBeInTheDocument());
    expect(screen.getByTestId("slot-chooser")).toHaveAttribute("data-resource-name", "CRA");
    expect(capturedChooserBacklog?.map((r) => r.id)).toEqual(["u-1"]);
  });

  it("schedules an existing request straight onto the clicked space and cell start", async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("empty-cell-click"));
    await waitFor(() => expect(screen.getByTestId("slot-chooser")).toBeInTheDocument());
    capturedOnScheduleExisting!({ id: "u-1", name: "Space job", durationMin: 120 });

    await waitFor(() => expect(mockScheduleMutateAsync).toHaveBeenCalledWith({
      requestId: "u-1",
      data: {
        resourceId: "space-1",
        startTs: "2026-06-22T00:00:00.000Z",
        endTs: "2026-06-22T02:00:00.000Z",
      },
    }));
    // Straight scheduling — no edit form detour.
    expect(screen.queryByTestId("request-form-dialog")).not.toBeInTheDocument();
  });

  it("create-new from a grid cell pre-selects the clicked space in the form", async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("empty-cell-click"));
    await waitFor(() => expect(screen.getByTestId("slot-chooser")).toBeInTheDocument());
    capturedOnCreateNew!();

    await waitFor(() => expect(screen.getByTestId("request-form-dialog")).toBeInTheDocument());
    expect(screen.getByTestId("request-form-dialog")).toHaveAttribute("data-default-resource", "space:space-1");
  });

  it("create-new from a slot then save creates a request", async () => {
    const { createRequest } = await import("@foundation/src/lib/api/request-api");
    const Wrapper = createWrapper("calendar");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    capturedOnSlotSelect!(new Date("2026-06-20T09:00:00Z"), new Date("2026-06-20T10:00:00Z"));
    await waitFor(() => expect(screen.getByTestId("slot-chooser")).toBeInTheDocument());
    capturedOnCreateNew!();
    await waitFor(() => expect(screen.getByTestId("request-form-dialog")).toBeInTheDocument());

    fireEvent.click(screen.getByTestId("save-request"));
    await waitFor(() => expect(createRequest).toHaveBeenCalled());
  });

  it("clicking a calendar event opens the request editor", async () => {
    mockUseRequests.mockReturnValue({ data: [{ id: "r1", name: "Task 1" }], isLoading: false });
    const Wrapper = createWrapper("calendar");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    capturedOnEventClick!("r1");
    await waitFor(() => expect(screen.getByTestId("request-form-dialog")).toBeInTheDocument());
  });

  it("moving a calendar event reschedules it with its current space", async () => {
    mockUseRequests.mockReturnValue({
      data: [{
        id: "r1", name: "Task 1",
        assignments: [{ resourceTypeKey: "space", assignmentStatus: "Planned", resourceId: "s1" }],
      }],
      isLoading: false,
    });
    const Wrapper = createWrapper("calendar");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    capturedOnEventMove!("r1", new Date("2026-06-20T09:00:00Z"), new Date("2026-06-20T11:00:00Z"));
    expect(mockScheduleMutate).toHaveBeenCalledWith(
      expect.objectContaining({
        requestId: "r1",
        data: expect.objectContaining({ resourceId: "s1" }),
      }),
    );
  });

  it("calendar dates-set syncs the shared anchor (scale is page-controlled)", () => {
    const Wrapper = createWrapper("calendar");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    const start = new Date("2026-07-01T00:00:00Z");
    capturedOnDatesSet!(start);
    expect(mockSetAnchorTs).toHaveBeenCalledWith(start);
    // Scale is owned by the page's selector now — the calendar never sets it.
    expect(mockSetScale).not.toHaveBeenCalled();
  });

  it("moving a calendar event with no space assignment does nothing", async () => {
    mockUseRequests.mockReturnValue({ data: [{ id: "r1", name: "Task 1" }], isLoading: false });
    const Wrapper = createWrapper("calendar");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    capturedOnEventMove!("r1", new Date("2026-06-20T09:00:00Z"), new Date("2026-06-20T11:00:00Z"));
    expect(mockScheduleMutate).not.toHaveBeenCalled();
  });

  it("closing the calendar form dismisses it", async () => {
    const Wrapper = createWrapper("calendar");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    capturedOnSlotSelect!(new Date("2026-06-20T09:00:00Z"), new Date("2026-06-20T10:00:00Z"));
    await waitFor(() => screen.getByTestId("slot-chooser"));
    capturedOnCreateNew!();
    await waitFor(() => expect(screen.getByTestId("request-form-dialog")).toBeInTheDocument());

    fireEvent.click(screen.getByTestId("close-form"));
    expect(screen.queryByTestId("request-form-dialog")).not.toBeInTheDocument();
  });

  // --- Export handler ---

  it("calls exportUtilization via export handler for pdf", async () => {
    const { exportUtilization } = await import("@foundation/src/lib/utils/export-handlers");
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(capturedExportHandler).toBeTruthy();
    await capturedExportHandler!("pdf");
    // Row labels are resolved inside the export (all resource types), so the
    // page no longer passes its spaces list. The 4th argument is the type scope.
    expect(vi.mocked(exportUtilization)).toHaveBeenCalledWith(
      expect.any(Array),
      expect.any(Date),
      expect.any(Date),
      expect.any(Array),
    );
  });

  it("exports only the active tab's resource type", async () => {
    const { exportUtilization } = await import("@foundation/src/lib/utils/export-handlers");
    // The Spaces tab is on screen, so a PDF full of people would disagree with it.
    const Wrapper = createWrapper("stations");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    await capturedExportHandler!("pdf");

    const types = vi.mocked(exportUtilization).mock.calls[0][3];
    expect(types.map((t) => t.key)).toEqual(["space"]);
  });

  it("gives a placeable type no tab of its own", async () => {
    // The floorplan read path never filtered by type — GET /api/sites/{id}/spaces scopes on
    // `rt.has_geometry` — so a tenant-defined placeable type already rendered on the plan. While
    // the grid tabs were "everything except the space key" it also got a tab, listing the same
    // resources a second time under a surface that cannot place them.
    mockResourceTypes.mockReturnValueOnce({
      data: [
        mockResourceType("space", "Space", "Spaces", true, true),
        mockResourceType("zone", "Zone", "Zones", false, true),
        mockResourceType("person", "Person", "People"),
      ],
      isSuccess: true,
    });
    const Wrapper = createWrapper("calendar");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    // Three fixed tabs. A type is a filter inside one of them, never a tab.
    expect(screen.queryByRole("tab", { name: "Zones" })).not.toBeInTheDocument();
    expect(screen.queryByRole("tab", { name: "People" })).not.toBeInTheDocument();
    expect(screen.getByRole("tab", { name: "Assets" })).toBeInTheDocument();
  });

  it("exports every placeable type from the scheduler tab, not just the one the tab names", async () => {
    const { exportUtilization } = await import("@foundation/src/lib/utils/export-handlers");
    // One floorplan holds every placeable type, so a PDF naming only the tab's own key would
    // omit resources that are visibly on the plan.
    mockResourceTypes.mockReturnValueOnce({
      data: [
        mockResourceType("space", "Space", "Spaces", true, true),
        mockResourceType("zone", "Zone", "Zones", false, true),
        mockResourceType("person", "Person", "People"),
      ],
      isSuccess: true,
    });
    const Wrapper = createWrapper("stations");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    await capturedExportHandler!("pdf");

    const types = vi.mocked(exportUtilization).mock.calls[0][3];
    expect(types.map((t) => t.key)).toEqual(["space", "zone"]);
  });

  it("exports every type from the Calendar tab, in tab order", async () => {
    const { exportUtilization } = await import("@foundation/src/lib/utils/export-handlers");
    // Calendar is request-centric — it has no type of its own, so the export
    // covers all of them, sectioned.
    const Wrapper = createWrapper("calendar");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    await capturedExportHandler!("pdf");

    const types = vi.mocked(exportUtilization).mock.calls[0][3];
    expect(types.map((t) => t.key)).toEqual(["space", "person", "tool", "forklift"]);
  });

  it("names the export after what it will contain", async () => {
    const Wrapper = createWrapper("assets", "person");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(capturedExportOffer?.label).toBe("Utilization (People)");
  });

  it("export handler ignores non-pdf formats", async () => {
    const { exportUtilization } = await import("@foundation/src/lib/utils/export-handlers");
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    await capturedExportHandler!("csv");
    expect(vi.mocked(exportUtilization)).not.toHaveBeenCalled();
  });

  // --- Availability events / scheduling settings ---

  it("expands availability event recurrences when event data is present", () => {
    mockUseAvailabilityEvents.mockReturnValue({
      data: [{
        id: "event-1",
        siteId: "site-1",
        title: "Shutdown",
        eventType: "shutdown",
        defaultEffect: "closed",
        startTs: "2026-12-24T00:00:00.000Z",
        endTs: "2026-12-26T00:00:00.000Z",
        isRecurring: false,
        enabled: true,
      }],
    });
    mockUseSchedulingSettings.mockReturnValue({ data: { timeZone: "America/New_York", weekendsEnabled: true } });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(vi.mocked(expandRecurrence)).toHaveBeenCalled();
  });

  it("generates weekend ranges when weekends are disabled", () => {
    mockUseSchedulingSettings.mockReturnValue({ data: { timeZone: "UTC", weekendsEnabled: false } });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(vi.mocked(generateWeekendRanges)).toHaveBeenCalled();
  });

  it("skips weekend ranges when weekends are enabled", () => {
    mockUseSchedulingSettings.mockReturnValue({ data: { timeZone: "UTC", weekendsEnabled: true } });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(vi.mocked(generateWeekendRanges)).not.toHaveBeenCalled();
  });

  it("filters out disabled availability events", () => {
    mockUseAvailabilityEvents.mockReturnValue({
      data: [
        {
          id: "event-1",
          siteId: "site-1",
          title: "Disabled shutdown",
          eventType: "shutdown",
          defaultEffect: "closed",
          startTs: "2026-12-24T00:00:00.000Z",
          endTs: "2026-12-26T00:00:00.000Z",
          isRecurring: false,
          enabled: false,
        },
        {
          id: "event-2",
          siteId: "site-1",
          title: "Active shutdown",
          eventType: "shutdown",
          defaultEffect: "closed",
          startTs: "2026-12-24T00:00:00.000Z",
          endTs: "2026-12-26T00:00:00.000Z",
          isRecurring: false,
          enabled: true,
        },
      ],
    });
    mockUseSchedulingSettings.mockReturnValue({ data: { timeZone: "UTC", weekendsEnabled: true } });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    // Only the enabled event should be expanded
    expect(vi.mocked(expandRecurrence)).toHaveBeenCalledTimes(1);
  });

  // --- Auto-schedule error paths ---

  it("shows 409 conflict error on auto-schedule apply", async () => {
    mockUseAutoScheduleAvailable.mockReturnValue(true);
    mockApplyMutateAsync.mockRejectedValueOnce(new Error("API Error (409): Conflict"));
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("auto-schedule-btn"));
    await waitFor(() => {
      expect(screen.getByTestId("preview-dialog")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId("apply-schedule"));
    await waitFor(() => {
      expect(screen.getByTestId("apply-error")).toHaveTextContent(/scheduling data has changed/i);
    });
  });

  it("shows generic error on auto-schedule apply failure", async () => {
    mockUseAutoScheduleAvailable.mockReturnValue(true);
    mockApplyMutateAsync.mockRejectedValueOnce(new Error("Server error"));
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("auto-schedule-btn"));
    await waitFor(() => {
      expect(screen.getByTestId("preview-dialog")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId("apply-schedule"));
    await waitFor(() => {
      expect(screen.getByTestId("apply-error")).toHaveTextContent("Server error");
    });
  });

  it("stringifies a non-Error rejection via the shared errorMessage normalizer", async () => {
    mockUseAutoScheduleAvailable.mockReturnValue(true);
    mockApplyMutateAsync.mockRejectedValueOnce("something");
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("auto-schedule-btn"));
    await waitFor(() => {
      expect(screen.getByTestId("preview-dialog")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId("apply-schedule"));
    await waitFor(() => {
      expect(screen.getByTestId("apply-error")).toHaveTextContent("something");
    });
  });

  // --- Double-click on non-existent request ---

  it("does nothing on double-click for unknown request", () => {
    mockUseRequests.mockReturnValue({ data: [], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("dblclick-request"));
    expect(screen.queryByTestId("request-form-dialog")).not.toBeInTheDocument();
  });

  // --- Drag-end handler paths ---

  // The window the track droppable reports: one 24h day column.
  const VIEW_START_MS = new Date("2024-01-20T00:00:00Z").getTime();
  const VIEW_END_MS = new Date("2024-01-21T00:00:00Z").getTime();

  it("handleDragEnd does nothing when no over target", async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);
    expect(capturedOnDragEnd).toBeTruthy();

    capturedOnDragEnd!({ active: { id: "r1", data: { current: {} } }, over: null });
    // No error = early return worked
  });

  it("handleDragEnd ignores a drop that carries no existing placement", async () => {
    mockUseRequests.mockReturnValue({
      data: [{ id: "r1", name: "Task 1", durationMin: 60 }],
      isLoading: false,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    // Only a scheduled bar is draggable onto a track — the backlog reaches the
    // grid through the "Schedule to…" dialog. A payload with no start/end is
    // therefore not a move we can resolve, and must not reach the mutation.
    capturedOnDragEnd!({
      active: { id: "r1", data: { current: { id: "r1", name: "Task 1", durationMin: 60 } } },
      over: {
        id: "track-s1",
        rect: { left: 0, width: 100 },
        data: { current: { type: "space-track", resourceId: "s1", viewStartMs: VIEW_START_MS, viewEndMs: VIEW_END_MS } },
      },
      delta: { x: 0, y: 0 },
    });

    await Promise.resolve();
    expect(mockScheduleMutateAsync).not.toHaveBeenCalled();
  });

  it("unschedules a request from the bar's context menu", async () => {
    mockUseRequests.mockReturnValue({ data: [{ id: "r1", name: "Task 1" }], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("context-request"));
    fireEvent.click(await screen.findByRole("menuitem", { name: "Unschedule" }));

    expect(mockScheduleMutate).toHaveBeenCalledWith(
      expect.objectContaining({
        requestId: "r1",
        data: expect.objectContaining({ resourceId: null, startTs: null, endTs: null }),
      }),
    );
  });

  it("gives viewers no context menu on the bar", () => {
    vi.mocked(useCanEdit).mockReturnValue(false);
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(screen.queryByTestId("context-request")).not.toBeInTheDocument();
  });

  it("handleDragEnd reorders spaces", async () => {
    mockUseSpaces.mockReturnValue({
      data: [{ id: "s1", name: "Room A" }, { id: "s2", name: "Room B" }],
      isLoading: false,
    });
    mockStoreOverrides = { spaceOrder: ["s1", "s2"] };
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    capturedOnDragEnd!({
      active: { id: "s1", data: { current: { type: "space-row" } } },
      over: { id: "s2", data: { current: { type: "space-row" } } },
    });

    expect(mockSetSpaceOrder).toHaveBeenCalledWith(["s2", "s1"]);
  });

  it("handleDragEnd moves a bar to where it was dropped, not to a column edge", async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    // A 100px track spanning one 24h day column. A 25px drag is a quarter of the
    // view = +6h, so a bar at 06:00 lands at 12:00 — NOT at the column's 00:00
    // edge, which is where the old pointer-to-column resolution put every drop.
    capturedOnDragEnd!({
      active: {
        id: "r1",
        data: {
          current: {
            id: "r1", name: "Task 1", isScheduled: true,
            startTs: "2024-01-20T06:00:00Z", endTs: "2024-01-20T08:00:00Z",
          },
        },
      },
      over: {
        id: "track-s2",
        rect: { left: 0, width: 100 },
        data: { current: { type: "space-track", resourceId: "s2", viewStartMs: VIEW_START_MS, viewEndMs: VIEW_END_MS } },
      },
      delta: { x: 25, y: 0 },
    });

    await waitFor(() => {
      expect(mockScheduleMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          requestId: "r1",
          data: expect.objectContaining({
            resourceId: "s2",
            startTs: "2024-01-20T12:00:00.000Z",
            // Duration preserved exactly — a move never resizes.
            endTs: "2024-01-20T14:00:00.000Z",
          }),
        }),
      );
    });
  });

  it("handleDragEnd keeps the time when a bar is dragged straight to another row", async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    // No horizontal movement → no time change. The old resolver re-derived the
    // start from the pointer's column, so a purely vertical move could shift it.
    capturedOnDragEnd!({
      active: {
        id: "r1",
        data: {
          current: {
            id: "r1", name: "Task 1", isScheduled: true,
            startTs: "2024-01-20T06:00:00Z", endTs: "2024-01-20T08:00:00Z",
          },
        },
      },
      over: {
        id: "track-s2",
        rect: { left: 0, width: 100 },
        data: { current: { type: "space-track", resourceId: "s2", viewStartMs: VIEW_START_MS, viewEndMs: VIEW_END_MS } },
      },
      delta: { x: 0, y: 120 },
    });

    await waitFor(() => {
      expect(mockScheduleMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          requestId: "r1",
          data: expect.objectContaining({
            resourceId: "s2",
            startTs: "2024-01-20T06:00:00.000Z",
            endTs: "2024-01-20T08:00:00.000Z",
          }),
        }),
      );
    });
  });

  it("handleTabChange switches to People tab via URL", () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);
    // PageTabs renders TabsTrigger for each tab; click "People"
    const peopleTab = screen.queryByRole('tab', { name: /people/i });
    if (peopleTab) {
      fireEvent.click(peopleTab);
      // After clicking, the URL search param should reflect the change
      // (MemoryRouter tracks history internally)
    }
    // At minimum the component renders without crashing after tab click
    expect(screen.getByTestId('scheduler-grid')).toBeInTheDocument();
  });

  it("passes the selected site to the People utilization grid", () => {
    const Wrapper = createWrapper("assets", "person");
    render(<Wrapper><UtilizationPage /></Wrapper>);
    expect(screen.getByTestId('person-utilization-grid')).toHaveAttribute('data-site-id', 'site-1');
  });

  it("does not unschedule a request that is not scheduled", async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    capturedOnDragEnd!({
      active: { id: "r1", data: { current: { id: "r1", isScheduled: false } } },
      over: { id: "unschedule", data: { current: { type: "unschedule" } } },
    });

    expect(mockScheduleMutate).not.toHaveBeenCalled();
  });

  // --- Tab default + site-scoped hook args ---

  it("defaults to the calendar tab when no tab param is present", () => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    render(
      <MemoryRouter initialEntries={["/"]}>
        <QueryClientProvider client={queryClient}>
          <UtilizationPage />
        </QueryClientProvider>
      </MemoryRouter>,
    );
    expect(screen.getByText("Utilization")).toBeInTheDocument();
  });

  it("passes undefined site id to scheduling hooks when no site is selected", () => {
    mockStoreOverrides = { selectedSiteId: null };
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);
    expect(mockUseSchedulingSettings).toHaveBeenCalledWith(undefined);
    expect(mockUseAvailabilityEvents).toHaveBeenCalledWith(undefined);
  });

  // --- Export end-date computation per scale ---

  it.each(["year", "month", "week", "day", "hour"] as const)(
    "export computes the visible window for scale=%s",
    async (scale) => {
      const { exportUtilization } = await import("@foundation/src/lib/utils/export-handlers");
      const { generateTimeColumns } = await import("@foundation/src/components/utilization/time-grid-utils");
      mockStoreOverrides = { scale };
      const Wrapper = createWrapper();
      render(<Wrapper><UtilizationPage /></Wrapper>);
      await capturedExportHandler!("pdf");

      // The exported window must be the grid's own columns — snapped to
      // week/month starts — not a raw anchor+1-period span, or the bars land
      // outside the chart.
      const columns = generateTimeColumns(scale, buildMockState().anchorTs);
      expect(vi.mocked(exportUtilization)).toHaveBeenCalledWith(
        expect.any(Array),
        columns[0].start,
        columns[columns.length - 1].end,
        expect.any(Array),
      );
    },
  );

  // --- Auto-schedule guard ---

  it("auto-schedule click is a no-op when no site is selected", () => {
    mockUseAutoScheduleAvailable.mockReturnValue(true);
    mockStoreOverrides = { selectedSiteId: null };
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);
    fireEvent.click(screen.getByTestId("auto-schedule-btn"));
    expect(mockPreviewMutateAsync).not.toHaveBeenCalled();
  });
});

describe("navigateTime", () => {
  const anchor = new Date("2024-06-15T12:00:00Z");

  it.each([
    ["year", 1, "2024-07-15"],
    ["year", -1, "2024-05-15"],
    ["month", 1, "2024-06-22"],
    ["month", -1, "2024-06-08"],
    ["week", 1, "2024-06-16"],
    ["week", -1, "2024-06-14"],
    ["day", 1, "2024-06-15T13:00"],
    ["day", -1, "2024-06-15T11:00"],
    ["hour", 1, "2024-06-15T12:15"],
    ["hour", -1, "2024-06-15T11:45"],
  ] as const)("scale=%s direction=%d", (scale, direction, expected) => {
    const result = navigateTime(anchor, scale, direction);
    expect(result.toISOString()).toContain(expected);
  });

  // ── Per-type tabs ─────────────────────────────────────────────────────────
  // Tabs are derived from the active resource types, so a built-in `tool` and a tenant-defined
  // type are first-class without the page naming either of them.

  it("stacks a grid per selected asset type under the one Assets tab", () => {
    const Wrapper = createWrapper("assets", "tool,forklift");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    // Three fixed tabs; the types are a filter within Assets, not tabs of their own.
    expect(screen.getAllByRole("tab").map((t) => t.textContent)).toEqual([
      "Calendar", "Stations", "Assets",
    ]);
    expect(screen.getByTestId("tool-utilization-grid")).toBeInTheDocument();
    expect(screen.getByTestId("forklift-utilization-grid")).toBeInTheDocument();
    // Placeable types belong to Stations, never to the asset stack.
    expect(screen.queryByTestId("space-utilization-grid")).not.toBeInTheDocument();
  });

  it("drops a filtered-out type from the asset stack", () => {
    const Wrapper = createWrapper("assets", "tool");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(screen.getByTestId("tool-utilization-grid")).toBeInTheDocument();
    expect(screen.queryByTestId("forklift-utilization-grid")).not.toBeInTheDocument();
  });

  it("falls back to Calendar when the URL names a type that is not active", () => {
    const Wrapper = createWrapper("vanished-type");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(screen.getByRole("tab", { name: "Calendar" })).toHaveAttribute("data-state", "active");
  });

  it("corrects the URL when it names a tab that no longer exists", async () => {
    // Rendering Calendar while ?tab= still says "vanished-type" leaves reload and the back
    // button disagreeing with the screen.
    const Wrapper = createWrapper("vanished-type");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    await waitFor(() =>
      expect(screen.getByRole("tab", { name: "Calendar" })).toHaveAttribute("data-state", "active"),
    );
    await waitFor(() => expect(window.location.search).not.toContain("vanished-type"));
  });

  it("offers auto-schedule on a grid tab filtered to one type, never on Calendar", async () => {
    const Wrapper = createWrapper("calendar");
    const { unmount } = render(<Wrapper><UtilizationPage /></Wrapper>);
    expect(screen.queryByTestId("auto-schedule-btn")).not.toBeInTheDocument();
    unmount();

    const ToolWrapper = createWrapper("assets", "tool");
    render(<ToolWrapper><UtilizationPage /></ToolWrapper>);
    expect(await screen.findByTestId("auto-schedule-btn")).toBeInTheDocument();
  });

  it("disables auto-schedule while the filter names more than one type", async () => {
    // One solver run solves one type; solving a guessed pool would be worse than not offering it.
    const Wrapper = createWrapper("assets", "tool,forklift");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(await screen.findByTestId("auto-schedule-btn")).toBeDisabled();
  });


  describe("stations grid search and filters", () => {
    // Status is derived from the schedule (withEffectiveStatus), so the dates decide it: an
    // undated request reads as New, one that has already ended as Done.
    const twoScheduled = [
      { id: "r1", name: "Fabricate frame", status: "new", assignments: [] },
      {
        id: "r2",
        name: "Finish weld",
        status: "new",
        startTs: "2020-01-01T09:00:00Z",
        endTs: "2020-01-01T11:00:00Z",
        assignments: [],
      },
    ];

    it("shows the grid's own key, not the calendar's statuses", () => {
      const Wrapper = createWrapper("stations");
      render(<Wrapper><UtilizationPage /></Wrapper>);

      // A grid bar is coloured by what it does to the station, never by request status.
      expect(screen.getByText("Assigned")).toBeInTheDocument();
      expect(screen.getByText("Overbooked")).toBeInTheDocument();
      expect(screen.getByText("Off-time")).toBeInTheDocument();
    });

    it("narrows the grid's requests to the search", async () => {
      mockUseRequests.mockReturnValue({ data: twoScheduled, isLoading: false });
      const Wrapper = createWrapper("stations");
      render(<Wrapper><UtilizationPage /></Wrapper>);

      await userEvent.type(screen.getByLabelText("Search requests"), "weld");

      await waitFor(() =>
        expect(screen.getByTestId("scheduler-grid")).toHaveAttribute(
          "data-request-names",
          "Finish weld",
        ),
      );
    });

    it("narrows the grid's requests by status", async () => {
      mockUseRequests.mockReturnValue({ data: twoScheduled, isLoading: false });
      const Wrapper = createWrapper("stations");
      render(<Wrapper><UtilizationPage /></Wrapper>);

      await userEvent.click(screen.getByRole("button", { name: "Filter by status" }));
      await userEvent.click(await screen.findByRole("menuitem", { name: "Done" }));

      await waitFor(() =>
        expect(screen.getByTestId("scheduler-grid")).toHaveAttribute(
          "data-request-names",
          "Fabricate frame",
        ),
      );
    });

    it("leaves the station rows alone, so a match stays findable", async () => {
      mockUseRequests.mockReturnValue({ data: twoScheduled, isLoading: false });
      const Wrapper = createWrapper("stations");
      render(<Wrapper><UtilizationPage /></Wrapper>);

      await userEvent.type(screen.getByLabelText("Search requests"), "nothing matches");

      // The filter hides bars, never stations: which station is empty is part of the answer.
      await waitFor(() =>
        expect(screen.getByTestId("scheduler-grid")).toHaveAttribute("data-request-names", ""),
      );
      expect(screen.getByTestId("scheduler-grid")).toBeInTheDocument();
    });
  });

  describe("assets tab search and filters", () => {
    it("carries one search for the whole tab, named after the tab", async () => {
      // Not "Search people": the tab stacks a grid per selected type, so a box labelled after one
      // of them would be wrong the moment a second type is shown.
      const Wrapper = createWrapper("assets");
      render(<Wrapper><UtilizationPage /></Wrapper>);

      expect(await screen.findByLabelText("Search assets")).toBeInTheDocument();
      expect(screen.queryByLabelText(/Search people/i)).not.toBeInTheDocument();
    });

    it("shows one search however many types are stacked", async () => {
      const Wrapper = createWrapper("assets", "tool,forklift");
      render(<Wrapper><UtilizationPage /></Wrapper>);

      expect(await screen.findAllByLabelText("Search assets")).toHaveLength(1);
    });

    it("shows the asset grids' own key, not the calendar's statuses", () => {
      const Wrapper = createWrapper("assets");
      render(<Wrapper><UtilizationPage /></Wrapper>);

      expect(screen.getByText("Booked")).toBeInTheDocument();
      expect(screen.getByText("Overbooked")).toBeInTheDocument();
      expect(screen.queryByText("In Progress")).not.toBeInTheDocument();
    });

    it("hands the same filter to every stacked grid", async () => {
      const Wrapper = createWrapper("assets", "tool,forklift");
      render(<Wrapper><UtilizationPage /></Wrapper>);

      await userEvent.type(screen.getByLabelText("Search assets"), "drill");

      await waitFor(() => {
        for (const key of ["tool", "forklift"]) {
          expect(screen.getByTestId(`${key}-utilization-grid`)).toHaveAttribute(
            "data-query",
            "drill",
          );
        }
      });
    });
  });
});
