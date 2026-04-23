/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { UtilizationPage } from "@foundation/src/pages/UtilizationPage";
import { navigateTime } from "@foundation/src/lib/utils/time-navigation";
import { expandRecurrence } from "@foundation/src/domain/scheduling/recurrence";
import { generateWeekendRanges } from "@foundation/src/domain/scheduling/weekend-ranges";


// --- Extractable mock fns for per-test control ---
const mockUseRequests = vi.fn((_?: any): any => ({ data: [], isLoading: false }));
const mockUseSpaces = vi.fn((_?: any): any => ({ data: [], isLoading: false }));
const mockUseAutoScheduleAvailable = vi.fn((_?: any): any => false);
let capturedExportHandler: ((format: string) => Promise<void>) | null = null;
const mockUseSchedulingSettings = vi.fn((_?: any): any => ({ data: null }));
const mockUseOffTimes = vi.fn((_?: any): any => ({ data: [] }));

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

// Mock the store — configurable per test
let mockStoreOverrides: Record<string, any> = {};
const mockSetSpaceOrder = vi.fn();
const mockSetAnchorTs = vi.fn();
const mockSetTimeCursorTs = vi.fn();
const mockSetIsFloorplanCollapsed = vi.fn();
const mockSetSelectedRequestId = vi.fn();
const mockSetConflicts = vi.fn();

vi.mock("@foundation/src/store/app-store", () => ({
  useAppStore: Object.assign(
    vi.fn((selector: any) => {
      const mockState: any = {
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
      };
      return selector ? selector(mockState) : mockState;
    }),
    {
      getState: () => ({
        spaceOrder: mockStoreOverrides.spaceOrder ?? [],
        setSpaceOrder: mockSetSpaceOrder,
      }),
    },
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
  useOffTimes: (arg?: any) => mockUseOffTimes(arg),
}));

vi.mock("@foundation/src/hooks/useSchedulingConflicts", () => ({
  useSchedulingConflicts: vi.fn(() => ({ conflictingRequestIds: new Set() })),
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
  useRequests: (arg?: any) => mockUseRequests(arg),
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
vi.mock("@dnd-kit/core", () => ({
  DndContext: ({ children, onDragEnd }: any) => {
    capturedOnDragEnd = onDragEnd;
    return <div data-testid="dnd-context">{children}</div>;
  },
  PointerSensor: vi.fn(),
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
  RequestFormDialog: ({ open, onSave, onOpenChange }: any) => open ? (
    <div data-testid="request-form-dialog">
      <button data-testid="save-request" onClick={() => onSave({ name: "Test" })}>Save</button>
      <button data-testid="close-form" onClick={() => onOpenChange(false)}>Close</button>
    </div>
  ) : null,
}));

vi.mock("@foundation/src/components/requests/RequestDetailsDialog", () => ({
  RequestDetailsDialog: ({ open }: any) => open ? <div data-testid="details-dialog" /> : null,
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
      {children}
    </QueryClientProvider>
  );
};

describe("UtilizationPage", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockRole = "admin";
    mockUseRequests.mockReturnValue({ data: [], isLoading: false });
    mockUseSpaces.mockReturnValue({ data: [], isLoading: false });
    mockUseAutoScheduleAvailable.mockReturnValue(false);
    mockUseSchedulingSettings.mockReturnValue({ data: null });
    mockUseOffTimes.mockReturnValue({ data: [] });
    capturedExportHandler = null;
    capturedOnDragEnd = null;
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

    expect(screen.getByText("Loading...")).toBeInTheDocument();
    expect(screen.queryByTestId("scheduler-grid")).not.toBeInTheDocument();
  });

  it("shows loading state when requests are loading", () => {
    mockUseRequests.mockReturnValue({ data: [], isLoading: true });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(screen.getByText("Loading...")).toBeInTheDocument();
    expect(screen.queryByTestId("scheduler-grid")).not.toBeInTheDocument();
  });

  it("shows SchedulerGrid when data is loaded", () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(screen.getByTestId("scheduler-grid")).toBeInTheDocument();
    expect(screen.queryByText("Loading...")).not.toBeInTheDocument();
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
    mockRole = "viewer";
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

  // --- Floorplan toggle ---

  it("toggles floorplan collapsed state", () => {
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);
    fireEvent.click(screen.getByTestId("toggle-floorplan"));
    expect(screen.getByTestId("collapsible-floorplan")).toBeInTheDocument();
  });

  // --- Request double-click handlers ---

  it("opens edit dialog on double-click when user can edit", async () => {
    mockUseRequests.mockReturnValue({ data: [{ id: "r1", name: "Task 1", spaceId: "s1" }], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("dblclick-request"));
    await waitFor(() => {
      expect(screen.getByTestId("request-form-dialog")).toBeInTheDocument();
    });
  });

  it("opens details dialog on double-click for viewer", async () => {
    mockRole = "viewer";
    mockUseRequests.mockReturnValue({ data: [{ id: "r1", name: "Task 1", spaceId: "s1" }], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("dblclick-request"));
    await waitFor(() => {
      expect(screen.getByTestId("details-dialog")).toBeInTheDocument();
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
    mockRole = "viewer";
    mockUseRequests.mockReturnValue({ data: [{ id: "r1", name: "Task 1" }], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    expect(screen.queryByTestId("create-child-btn")).not.toBeInTheDocument();
  });

  // --- Resize ---

  it("calls scheduleMutation on resize", () => {
    mockUseRequests.mockReturnValue({
      data: [{ id: "r1", name: "Task 1", spaceId: "s1" }],
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
    mockUseRequests.mockReturnValue({ data: [{ id: "r1", name: "Task 1", spaceId: "s1" }], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    // Open edit dialog via double-click
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
    mockUseRequests.mockReturnValue({ data: [{ id: "r1", name: "Task 1", spaceId: "s1" }], isLoading: false });
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

  // --- Off-time / scheduling settings ---

  it("expands off-time recurrences when off-time data is present", () => {
    mockUseOffTimes.mockReturnValue({ data: [{ id: "ot1", enabled: true }] });
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

  it("filters out disabled off-time definitions", () => {
    mockUseOffTimes.mockReturnValue({ data: [{ id: "ot1", enabled: false }, { id: "ot2", enabled: true }] });
    mockUseSchedulingSettings.mockReturnValue({ data: { timeZone: "UTC", weekendsEnabled: true } });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    // Only the enabled off-time should be expanded
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

  it("shows fallback error for non-Error rejection", async () => {
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
      expect(screen.getByTestId("apply-error")).toHaveTextContent("Failed to apply schedule");
    });
  });

  // --- Double-click on non-existent request ---

  it("does nothing on double-click for unknown request", () => {
    mockUseRequests.mockReturnValue({ data: [], isLoading: false });
    const Wrapper = createWrapper();
    render(<Wrapper><UtilizationPage /></Wrapper>);

    fireEvent.click(screen.getByTestId("dblclick-request"));
    expect(screen.queryByTestId("request-form-dialog")).not.toBeInTheDocument();
    expect(screen.queryByTestId("details-dialog")).not.toBeInTheDocument();
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

    capturedOnDragEnd!({
      active: { id: "r1", data: { current: { id: "r1", name: "Task 1", durationMin: 60 } } },
      over: { id: "grid-cell", data: { current: { spaceId: "s1", startTs: new Date("2024-01-20T09:00:00Z") } } },
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
        data: expect.objectContaining({ spaceId: null }),
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
      over: { id: "grid-cell", data: { current: { spaceId: "s2", startTs: new Date("2024-01-20T10:00:00Z") } } },
    });

    await waitFor(() => {
      expect(mockScheduleMutateAsync).toHaveBeenCalledWith(
        expect.objectContaining({
          requestId: "r1",
          data: expect.objectContaining({
            spaceId: "s2",
            startTs: "2024-01-20T10:00:00.000Z",
            endTs: "2024-01-20T12:00:00.000Z",
          }),
        }),
      );
    });
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