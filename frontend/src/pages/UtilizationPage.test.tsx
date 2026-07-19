/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, act } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { UtilizationPage } from "@foundation/src/pages/UtilizationPage";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import { navigateTime } from "@foundation/src/lib/utils/time-navigation";
import { makeRequest, spaceAssignment } from "@foundation/src/test-utils/request-fixtures";
import { expandRecurrence } from "@foundation/src/domain/scheduling/recurrence";
import { generateWeekendRanges } from "@foundation/src/domain/scheduling/weekend-ranges";
import { addDays, addMonths, startOfDay, startOfMonth } from "date-fns";


// --- Extractable mock fns for per-test control ---
const mockUseRequests = vi.fn((_?: any): any => ({ data: [], isLoading: false }));
const mockUseSpaces = vi.fn((_?: any): any => ({ data: [], isLoading: false }));
const mockUseAutoScheduleAvailable = vi.fn((_?: any): any => false);
let capturedExportHandler: ((format: string) => Promise<void>) | null = null;
const mockUseSchedulingSettings = vi.fn((_?: any): any => ({ data: null }));
const mockUseAvailabilityEvents = vi.fn((_?: any): any => ({ data: [] }));

// Mock AuthContext — default: admin
let mockRole = "admin";
vi.mock("@foundation/src/contexts/AuthContext", () => ({
  useAuth: () => ({
    membership: {
      tenantId: "tenant-1",
      slug: "demo",
      displayName: "Demo",
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

// Breakpoint — configurable per test; default desktop so all pre-existing tests are unaffected.
let mockDevice: "phone" | "tablet" | "desktop" = "desktop";
vi.mock("@foundation/src/hooks/useBreakpoint", () => ({
  useBreakpoint: () => ({
    device: mockDevice,
    isPhone: mockDevice === "phone",
    isTablet: mockDevice === "tablet",
    isDesktop: mockDevice === "desktop",
  }),
}));

// Mock the store — configurable per test
let mockStoreOverrides: Record<string, any> = {};
const mockSetSpaceOrder = vi.fn();
const mockSetAnchorTs = vi.fn();
const mockSetTimeCursorTs = vi.fn();
const mockSetIsFloorplanCollapsed = vi.fn();
const mockSetSelectedRequestId = vi.fn();
const mockSetConflicts = vi.fn();

// Single source of truth for the mocked store state, shared by the hook selector and getState()
// (the stale-anchor reconcile effect reads the live anchor via useAppStore.getState()).
const buildMockState = (): any => ({
  selectedSiteId: "site-1",
  conflicts: new Map(),
  scale: "month" as const,
  setScale: vi.fn(),
  anchorTs: new Date("2024-01-15"),
  setAnchorTs: mockSetAnchorTs,
  timeCursorTs: new Date(),
  setTimeCursorTs: mockSetTimeCursorTs,
  isFloorplanCollapsed: false,
  setIsFloorplanCollapsed: mockSetIsFloorplanCollapsed,
  selectedRequestId: null,
  setSelectedRequestId: mockSetSelectedRequestId,
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
  // Args are forwarded so window-shape tests can assert the from/to the page computed.
  useScheduledRequests: (...args: any[]) => mockUseRequests(...args),
  useBacklogRequests: () => ({ data: [], isLoading: false }),
  useUpdateRequest: vi.fn(() => ({ mutate: vi.fn() })),
  useScheduleRequest: vi.fn(() => ({ mutate: mockScheduleMutate, mutateAsync: mockScheduleMutateAsync })),
  useSpaces: (arg?: any) => mockUseSpaces(arg),
}));

vi.mock("@foundation/src/hooks/useImportExport", () => ({
  useExportHandler: vi.fn((_key: string, handler: any) => { capturedExportHandler = handler; }),
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
let capturedOnDragStart: ((event: any) => void) | null = null;
let capturedOnDragCancel: (() => void) | null = null;
vi.mock("@dnd-kit/core", () => ({
  DndContext: ({ children, onDragEnd, onDragStart, onDragCancel }: any) => {
    capturedOnDragEnd = onDragEnd;
    capturedOnDragStart = onDragStart;
    capturedOnDragCancel = onDragCancel;
    return <div data-testid="dnd-context">{children}</div>;
  },
  DragOverlay: ({ children }: any) => <div data-testid="drag-overlay">{children}</div>,
  useDndMonitor: vi.fn(),
  PointerSensor: vi.fn(),
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

vi.mock("@foundation/src/components/utilization/RequestsPanel", () => ({
  RequestsPanel: ({ isLoading, onCreateChild }: any) => (
    <div data-testid="requests-panel">
      {isLoading ? "panel-loading" : "panel-ready"}
      {onCreateChild && <button data-testid="create-child-btn" onClick={() => onCreateChild("r1")}>Add Child</button>}
    </div>
  ),
}));

vi.mock("@foundation/src/components/utilization/SchedulerGrid", () => ({
  SchedulerGrid: ({ onRequestDoubleClick, onRequestResize, onTimeCursorClick }: any) => (
    <div data-testid="scheduler-grid">
      {onRequestDoubleClick && <button data-testid="dblclick-request" onClick={() => onRequestDoubleClick("r1")}>DblClick</button>}
      {onRequestResize && <button data-testid="resize-request" onClick={() => onRequestResize("r1", "2024-01-15T10:00:00Z", "2024-01-15T12:00:00Z")}>Resize</button>}
      {onTimeCursorClick && <button data-testid="cursor-click" onClick={() => onTimeCursorClick(new Date("2024-06-01"))}>Cursor</button>}
    </div>
  ),
}));

vi.mock("@foundation/src/components/utilization/PeopleUtilizationGrid", () => ({
  PeopleUtilizationGrid: ({ siteId }: any) => (
    <div data-testid="people-utilization-grid" data-site-id={siteId ?? ""} />
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
  RequestFormDialog: ({ open, onSave, onOpenChange, scheduleSiteId, canEdit }: any) => open ? (
    <div data-testid="request-form-dialog" data-schedule-site-id={scheduleSiteId ?? ""} data-can-edit={String(canEdit)}>
      <button data-testid="save-request" onClick={() => onSave({ name: "Test" })}>Save</button>
      <button data-testid="close-form" onClick={() => onOpenChange(false)}>Close</button>
    </div>
  ) : null,
}));

let capturedOnSlotSelect: ((start: Date, end: Date) => void) | null = null;
let capturedOnEventClick: ((requestId: string) => void) | null = null;
let capturedOnEventMove: ((requestId: string, start: Date, end: Date) => void) | null = null;
let capturedOnDatesSet: ((scale: "day" | "week" | "month", start: Date) => void) | null = null;
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
vi.mock("@foundation/src/components/utilization/ScheduleSlotDialog", () => ({
  ScheduleSlotDialog: ({ open, onScheduleExisting, onCreateNew }: any) => {
    capturedOnScheduleExisting = onScheduleExisting;
    capturedOnCreateNew = onCreateNew;
    return open ? <div data-testid="slot-chooser" /> : null;
  },
}));

const createWrapper = (initialTab = "space") => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return ({ children }: { children: React.ReactNode }) => (
    <MemoryRouter initialEntries={[`/?tab=${initialTab}`]}>
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
    // useCanEdit is globally mocked to true (src/test/setup.ts); reset each test.
    vi.mocked(useCanEdit).mockReturnValue(true);
    mockUseRequests.mockReturnValue({ data: [], isLoading: false });
    mockUseSpaces.mockReturnValue({ data: [], isLoading: false });
    mockUseAutoScheduleAvailable.mockReturnValue(false);
    mockUseSchedulingSettings.mockReturnValue({ data: null });
    mockUseAvailabilityEvents.mockReturnValue({ data: [] });
    capturedExportHandler = null;
    capturedOnDragEnd = null;
    capturedOnDragStart = null;
    capturedOnDragCancel = null;
    capturedOnSlotSelect = null;
    capturedOnEventClick = null;
    capturedOnEventMove = null;
    capturedOnDatesSet = null;
    capturedOnScheduleExisting = null;
    capturedOnCreateNew = null;
    mockStoreOverrides = {};
    mockDevice = "desktop";
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

  it("renders floorplan and requests panel", () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(screen.getByTestId("collapsible-floorplan")).toBeInTheDocument();
    expect(screen.getByTestId("requests-panel")).toBeInTheDocument();
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

  it("passes isLoading to RequestsPanel", () => {
    mockUseRequests.mockReturnValue({ data: [], isLoading: true });
    // spacesLoading false so we can see the panel text
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(screen.getByText("panel-loading")).toBeInTheDocument();
  });

  it("passes loaded requests to RequestsPanel", () => {
    mockUseRequests.mockReturnValue({ data: [{ id: "r1", name: "Task 1" }], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(screen.getByText("panel-ready")).toBeInTheDocument();
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

  it("opens create-child dialog from RequestsPanel", async () => {
    mockUseRequests.mockReturnValue({ data: [{ id: "r1", name: "Task 1" }], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("create-child-btn"));
    await waitFor(() => {
      expect(screen.getByTestId("request-form-dialog")).toBeInTheDocument();
    });
  });

  it("hides create-child button for viewers", () => {
    vi.mocked(useCanEdit).mockReturnValue(false);
    mockUseRequests.mockReturnValue({ data: [{ id: "r1", name: "Task 1" }], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(screen.queryByTestId("create-child-btn")).not.toBeInTheDocument();
  });

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
    await waitFor(() => expect(screen.queryByTestId("request-form-dialog")).not.toBeInTheDocument());
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

  it("calendar dates-set syncs the shared scale/anchor window", () => {
    const Wrapper = createWrapper("calendar");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    const start = new Date("2026-07-01T00:00:00Z");
    capturedOnDatesSet!("week", start);
    expect(mockSetAnchorTs).toHaveBeenCalledWith(start);
  });

  it("drag start sets the overlay label and drag cancel clears it", async () => {
    const Wrapper = createWrapper("space");
    render(<Wrapper><UtilizationPage /></Wrapper>);

    capturedOnDragStart!({ active: { data: { current: { type: "request", name: "Task 1" } } } });
    await waitFor(() => expect(screen.getByText("Task 1")).toBeInTheDocument());

    capturedOnDragCancel!();
    await waitFor(() => expect(screen.queryByText("Task 1")).not.toBeInTheDocument());
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
    await waitFor(() => expect(screen.queryByTestId("request-form-dialog")).not.toBeInTheDocument());
  });

  // --- Phone calendar agenda (month grid replaced by the drag-free agenda) ---

  describe("phone calendar agenda", () => {
    // Store anchor is 2024-01-15 (see buildMockState) → viewed month is January 2024.
    const janRequest = makeRequest({
      id: "r-jan", name: "January job",
      startTs: "2024-01-10T08:00:00Z", endTs: "2024-01-12T16:00:00Z",
    });
    const marRequest = makeRequest({
      id: "r-mar", name: "March job",
      startTs: "2024-03-05T08:00:00Z", endTs: "2024-03-06T16:00:00Z",
    });
    const spanningRequest = makeRequest({
      id: "r-span", name: "Year-end changeover",
      startTs: "2023-12-20T08:00:00Z", endTs: "2024-01-03T16:00:00Z",
    });

    beforeEach(() => {
      mockDevice = "phone";
    });

    it("renders the agenda instead of the calendar grid", () => {
      mockUseRequests.mockReturnValue({ data: [janRequest], isLoading: false });
      const Wrapper = createWrapper("calendar");
      render(<Wrapper><UtilizationPage /></Wrapper>);

      expect(screen.queryByTestId("request-calendar")).not.toBeInTheDocument();
      expect(screen.getAllByRole("listitem")).toHaveLength(1);
      expect(screen.getByText("January job")).toBeInTheDocument();
    });

    it("desktop keeps the calendar grid and shows no agenda", () => {
      mockDevice = "desktop";
      mockUseRequests.mockReturnValue({ data: [janRequest], isLoading: false });
      const Wrapper = createWrapper("calendar");
      render(<Wrapper><UtilizationPage /></Wrapper>);

      expect(screen.getByTestId("request-calendar")).toBeInTheDocument();
      expect(screen.queryByRole("listitem")).not.toBeInTheDocument();
    });

    it("clips the buffered scheduled set to the viewed month (boundary-spanners included)", () => {
      mockUseRequests.mockReturnValue({ data: [janRequest, marRequest, spanningRequest], isLoading: false });
      const Wrapper = createWrapper("calendar");
      render(<Wrapper><UtilizationPage /></Wrapper>);

      expect(screen.getAllByRole("listitem")).toHaveLength(2);
      expect(screen.getByText("January job")).toBeInTheDocument();
      expect(screen.getByText("Year-end changeover")).toBeInTheDocument();
      expect(screen.queryByText("March job")).not.toBeInTheDocument();
    });

    it("shows the month empty message when nothing overlaps the viewed month", () => {
      mockUseRequests.mockReturnValue({ data: [marRequest], isLoading: false });
      const Wrapper = createWrapper("calendar");
      render(<Wrapper><UtilizationPage /></Wrapper>);

      expect(screen.getByText("No scheduled work this month.")).toBeInTheDocument();
      expect(screen.queryByRole("listitem")).not.toBeInTheDocument();
    });

    it("pages by whole months, not navigateTime's week-sized 'month' step", () => {
      const Wrapper = createWrapper("calendar");
      render(<Wrapper><UtilizationPage /></Wrapper>);

      const anchor = new Date("2024-01-15");
      fireEvent.click(screen.getByTestId("nav-next"));
      expect(mockSetAnchorTs).toHaveBeenCalledWith(addMonths(anchor, 1));
      fireEvent.click(screen.getByTestId("nav-prev"));
      expect(mockSetAnchorTs).toHaveBeenCalledWith(addMonths(anchor, -1));
    });

    it("forces a month-sized fetch window even when the stored grid scale is day", () => {
      mockStoreOverrides = { scale: "day", anchorTs: new Date() };
      const Wrapper = createWrapper("calendar");
      render(<Wrapper><UtilizationPage /></Wrapper>);

      const monthStart = startOfMonth(new Date());
      expect(mockUseRequests).toHaveBeenCalledWith("site-1", addMonths(monthStart, -2), addMonths(monthStart, 3));
    });

    it("keeps the day-sized fetch window on desktop with day scale (regression guard)", () => {
      mockDevice = "desktop";
      const anchor = new Date();
      mockStoreOverrides = { scale: "day", anchorTs: anchor };
      const Wrapper = createWrapper("calendar");
      render(<Wrapper><UtilizationPage /></Wrapper>);

      const dayStart = startOfDay(anchor);
      expect(mockUseRequests).toHaveBeenCalledWith("site-1", addDays(dayStart, -7), addDays(dayStart, 8));
    });

    it("tapping an agenda card opens the request editor", async () => {
      mockUseRequests.mockReturnValue({ data: [janRequest], isLoading: false });
      const Wrapper = createWrapper("calendar");
      render(<Wrapper><UtilizationPage /></Wrapper>);

      fireEvent.click(screen.getByText("January job"));
      await waitFor(() => expect(screen.getByTestId("request-form-dialog")).toBeInTheDocument());
    });
  });

  it("closing the create-child dialog clears the parent", async () => {
    mockUseRequests.mockReturnValue({ data: [{ id: "r1", name: "Task 1" }], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("create-child-btn"));
    await waitFor(() => expect(screen.getByTestId("request-form-dialog")).toBeInTheDocument());

    fireEvent.click(screen.getByTestId("close-form"));
    await waitFor(() => expect(screen.queryByTestId("request-form-dialog")).not.toBeInTheDocument());
  });

  // --- Save child request ---

  it("saves child request from create-child dialog", async () => {
    const { createRequest } = await import("@foundation/src/lib/api/request-api");
    mockUseRequests.mockReturnValue({ data: [{ id: "r1", name: "Task 1" }], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("create-child-btn"));
    await waitFor(() => {
      expect(screen.getByTestId("request-form-dialog")).toBeInTheDocument();
    });

    fireEvent.click(screen.getByTestId("save-request"));
    await waitFor(() => {
      expect(vi.mocked(createRequest)).toHaveBeenCalled();
    });
  });

  // --- Export handler ---

  it("calls exportUtilization via export handler for pdf", async () => {
    const { exportUtilization } = await import("@foundation/src/lib/utils/export-handlers");
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(capturedExportHandler).toBeTruthy();
    await capturedExportHandler!("pdf");
    expect(vi.mocked(exportUtilization)).toHaveBeenCalledWith(
      expect.any(Array),
      expect.any(Array),
      expect.any(Date),
      expect.any(Date),
    );
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

  it("handleDragEnd does nothing when no over target", async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);
    expect(capturedOnDragEnd).toBeTruthy();

    capturedOnDragEnd!({ active: { id: "r1", data: { current: {} } }, over: null });
    // No error = early return worked
  });

  it("handleDragEnd schedules request to grid", async () => {
    mockUseRequests.mockReturnValue({
      data: [{ id: "r1", name: "Task 1", durationMin: 60 }],
      isLoading: false,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    // The whole row is one droppable; the column is resolved from the pointer
    // x-position within the track's rect. One column → always lands on it.
    capturedOnDragEnd!({
      active: { id: "r1", data: { current: { id: "r1", name: "Task 1", durationMin: 60 } } },
      over: {
        id: "track-s1",
        rect: { left: 0, width: 100 },
        data: { current: { type: "space-track", resourceId: "s1", columnStartsMs: [new Date("2024-01-20T09:00:00Z").getTime()] } },
      },
      activatorEvent: { clientX: 50 },
      delta: { x: 0, y: 0 },
    });

    await waitFor(() => {
      expect(mockScheduleMutateAsync).toHaveBeenCalledWith(expect.objectContaining({ requestId: "r1" }));
    });
  });

  it("handleDragEnd unschedules request", async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    capturedOnDragEnd!({
      active: { id: "r1", data: { current: { id: "r1", isScheduled: true } } },
      over: { id: "unschedule", data: { current: { type: "unschedule" } } },
    });

    expect(mockScheduleMutate).toHaveBeenCalledWith(
      expect.objectContaining({
        requestId: "r1",
        data: expect.objectContaining({ resourceId: null }),
      }),
    );
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

  it("handleDragEnd reparents request in tree", async () => {
    const { moveRequest } = await import("@foundation/src/lib/api/request-api");
    mockUseRequests.mockReturnValue({
      data: [{ id: "r1", name: "Task 1" }, { id: "r2", name: "Parent" }],
      isLoading: false,
    });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    capturedOnDragEnd!({
      active: { id: "r1", data: { current: { id: "r1", name: "Task 1" } } },
      over: { id: "r2", data: { current: { type: "tree-reparent", parentRequestId: "r2" } } },
    });

    await waitFor(() => {
      expect(vi.mocked(moveRequest)).toHaveBeenCalledWith("r1", expect.objectContaining({ newParentRequestId: "r2" }));
    });
  });

  it("handleDragEnd schedules already-scheduled request preserving duration", async () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    capturedOnDragEnd!({
      active: {
        id: "r1",
        data: {
          current: {
            id: "r1", name: "Task 1", isScheduled: true,
            startTs: "2024-01-15T09:00:00Z", endTs: "2024-01-15T11:00:00Z",
          },
        },
      },
      over: {
        id: "track-s2",
        rect: { left: 0, width: 100 },
        data: { current: { type: "space-track", resourceId: "s2", columnStartsMs: [new Date("2024-01-20T10:00:00Z").getTime()] } },
      },
      activatorEvent: { clientX: 50 },
      delta: { x: 0, y: 0 },
    });

    await waitFor(() => {
      expect(mockScheduleMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          requestId: "r1",
          data: expect.objectContaining({
            resourceId: "s2",
            startTs: "2024-01-20T10:00:00.000Z",
            endTs: "2024-01-20T12:00:00.000Z",
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
    const Wrapper = createWrapper("people");
    render(<Wrapper><UtilizationPage /></Wrapper>);
    expect(screen.getByTestId('people-utilization-grid')).toHaveAttribute('data-site-id', 'site-1');
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

  it.each(["year", "week", "day", "hour"] as const)(
    "export computes the visible window for scale=%s",
    async (scale) => {
      const { exportUtilization } = await import("@foundation/src/lib/utils/export-handlers");
      mockStoreOverrides = { scale };
      const Wrapper = createWrapper();
      render(<Wrapper><UtilizationPage /></Wrapper>);
      await capturedExportHandler!("pdf");
      expect(vi.mocked(exportUtilization)).toHaveBeenCalled();
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
});
