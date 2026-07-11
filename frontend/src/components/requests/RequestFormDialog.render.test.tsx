import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RequestFormDialog } from "./RequestFormDialog";
import type { Request } from "@foundation/src/types/requests";
import type { Site } from "@foundation/src/types/site";

// --- Mock the data-loading boundary (network) and site hooks -------------------
const useSitesMock = vi.fn(() => ({ data: [] as Site[] }));
const useIsMultiSiteMock = vi.fn(() => false);
vi.mock("@foundation/src/hooks/useSites", () => ({
  useSites: () => useSitesMock(),
  useIsMultiSite: () => useIsMultiSiteMock(),
}));

vi.mock("@foundation/src/store/app-store", () => ({
  useAppStore: vi.fn((selector: (s: { selectedSiteId: string }) => unknown) =>
    selector({ selectedSiteId: "site-1" }),
  ),
}));

// API mocks are overridable per-test via these handles.
const apiMocks = vi.hoisted(() => ({
  getCriteria: vi.fn(() => Promise.resolve([] as unknown[])),
  getRequestChildren: vi.fn(() => Promise.resolve([] as unknown[])),
  getTemplates: vi.fn(() => Promise.resolve([] as unknown[])),
  getSpaces: vi.fn(() => Promise.resolve([] as unknown[])),
  createRequest: vi.fn(() => Promise.resolve({} as unknown)),
  moveRequest: vi.fn(() => Promise.resolve({} as unknown)),
}));
vi.mock("@foundation/src/lib/api/criteria-api", () => ({
  getCriteria: apiMocks.getCriteria,
}));
vi.mock("@foundation/src/lib/api/request-api", () => ({
  getRequestChildren: apiMocks.getRequestChildren,
  createRequest: apiMocks.createRequest,
  moveRequest: apiMocks.moveRequest,
}));
vi.mock("@foundation/src/lib/api/template-api", () => ({
  getTemplates: apiMocks.getTemplates,
}));
vi.mock("@foundation/src/lib/api/space-api", () => ({
  getSpaces: apiMocks.getSpaces,
}));

// --- Stub the heavy section children so the test targets the orchestrator ------
// RequestScheduleSection exposes buttons that seed schedule windows via setField,
// letting us drive the validation/warning paths the dialog owns. Constraint section
// seeds an out-of-order constraint window.
vi.mock("./RequestScheduleSection", () => ({
  RequestScheduleSection: ({
    setField,
  }: {
    setField: (field: string, value: unknown) => void;
  }) => (
    <>
      <button
        type="button"
        data-testid="seed-window"
        onClick={() => {
          setField("startDate", "2026-01-06");
          setField("startTime", "08:00");
          setField("endDate", "2026-01-06");
          setField("endTime", "09:00");
        }}
      >
        seed window
      </button>
      <button
        type="button"
        data-testid="seed-bad-order"
        onClick={() => {
          setField("startDate", "2026-01-06");
          setField("startTime", "10:00");
          setField("endDate", "2026-01-06");
          setField("endTime", "09:00");
        }}
      >
        seed bad order
      </button>
      <button
        type="button"
        data-testid="seed-start-only"
        onClick={() => {
          setField("startDate", "2026-01-06");
          setField("startTime", "08:00");
        }}
      >
        seed start only
      </button>
    </>
  ),
}));
vi.mock("./RequestConstraintsSection", () => ({
  RequestConstraintsSection: ({
    title,
    description,
  }: {
    title?: string;
    description?: string;
  }) => (
    <div data-testid="constraints">
      {title && <h4>{title}</h4>}
      {description && <p>{description}</p>}
    </div>
  ),
}));
vi.mock("./RequestRequirementsSection", () => ({
  RequestRequirementsSection: () => <div data-testid="requirements" />,
}));
vi.mock("./RequestPeopleSection", () => ({
  RequestPeopleSection: () => <div data-testid="people" />,
}));
vi.mock("@foundation/src/components/requests/RequestIconSelector", () => ({
  RequestIconSelector: ({
    onChange,
  }: {
    onChange: (next: string) => void;
  }) => (
    <button type="button" data-testid="pick-icon" onClick={() => onChange("star")}>
      icon
    </button>
  ),
}));
vi.mock("@foundation/src/lib/core/logger", () => ({
  logger: { error: vi.fn(), info: vi.fn(), warn: vi.fn(), debug: vi.fn() },
}));

const SITE_A: Site = {
  id: "site-1",
  code: "A",
  name: "Site A",
  createdAt: "2026-01-01T00:00:00Z",
  updatedAt: "2026-01-01T00:00:00Z",
};
const SITE_B: Site = { ...SITE_A, id: "site-2", code: "B", name: "Site B" };

const EXISTING: Request = {
  id: "req-1",
  name: "Existing Request",
  planningMode: "leaf",
  status: "new",
  minimalDurationValue: 2,
  minimalDurationUnit: "hours",
} as Request;

// A group with two scheduled children — for breadcrumb / Children tab / rollups.
const GROUP: Request = {
  id: "grp-1",
  name: "Group A",
  planningMode: "summary",
  status: "new",
  sortOrder: 0,
  minimalDurationValue: 0,
  minimalDurationUnit: "hours",
  createdAt: "2026-01-01T00:00:00Z",
  requirements: [],
  assignments: [],
} as unknown as Request;
const CHILD_1: Request = {
  id: "c-1",
  name: "Child One",
  planningMode: "leaf",
  status: "scheduled",
  parentRequestId: "grp-1",
  sortOrder: 0,
  minimalDurationValue: 2,
  minimalDurationUnit: "hours",
  startTs: "2026-02-01T09:00:00Z",
  endTs: "2026-02-01T11:00:00Z",
  createdAt: "2026-01-01T00:00:00Z",
  requirements: [],
  assignments: [],
} as unknown as Request;
const CHILD_2: Request = {
  id: "c-2",
  name: "Child Two",
  planningMode: "leaf",
  status: "new",
  parentRequestId: "grp-1",
  sortOrder: 1,
  minimalDurationValue: 3,
  minimalDurationUnit: "hours",
  startTs: "2026-02-02T09:00:00Z",
  endTs: "2026-02-02T12:00:00Z",
  createdAt: "2026-01-01T00:00:00Z",
  requirements: [],
  assignments: [],
} as unknown as Request;
const TREE: Request[] = [GROUP, CHILD_1, CHILD_2];

// Parentless task/group (candidates), a sibling group with its own child (not a
// candidate), and the current group's existing child — for the "Add existing" picker.
const PARENTLESS_TASK: Request = {
  id: "task-parentless",
  name: "Parentless Task",
  planningMode: "leaf",
  status: "new",
  sortOrder: 0,
  minimalDurationValue: 1,
  minimalDurationUnit: "hours",
  createdAt: "2026-01-01T00:00:00Z",
  requirements: [],
  assignments: [],
} as unknown as Request;
const PARENTLESS_GROUP: Request = {
  id: "group-parentless",
  name: "Parentless Group",
  planningMode: "summary",
  status: "new",
  sortOrder: 1,
  minimalDurationValue: 0,
  minimalDurationUnit: "hours",
  createdAt: "2026-01-01T00:00:00Z",
  requirements: [],
  assignments: [],
} as unknown as Request;
const OTHER_GROUP: Request = {
  id: "grp-2",
  name: "Group B",
  planningMode: "summary",
  status: "new",
  sortOrder: 2,
  minimalDurationValue: 0,
  minimalDurationUnit: "hours",
  createdAt: "2026-01-01T00:00:00Z",
  requirements: [],
  assignments: [],
} as unknown as Request;
const TASK_UNDER_OTHER_GROUP: Request = {
  id: "task-under-other",
  name: "Task Under Other Group",
  planningMode: "leaf",
  status: "new",
  parentRequestId: "grp-2",
  sortOrder: 0,
  minimalDurationValue: 1,
  minimalDurationUnit: "hours",
  createdAt: "2026-01-01T00:00:00Z",
  requirements: [],
  assignments: [],
} as unknown as Request;
const TREE_WITH_CANDIDATES: Request[] = [
  GROUP,
  CHILD_1,
  PARENTLESS_TASK,
  PARENTLESS_GROUP,
  OTHER_GROUP,
  TASK_UNDER_OTHER_GROUP,
];

function renderDialog(props?: Partial<React.ComponentProps<typeof RequestFormDialog>>) {
  const onSave = vi.fn(() => Promise.resolve());
  const onOpenChange = vi.fn();
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  render(
    <QueryClientProvider client={queryClient}>
      <RequestFormDialog
        open
        onOpenChange={onOpenChange}
        onSave={onSave}
        {...props}
      />
    </QueryClientProvider>,
  );
  return { onSave, onOpenChange };
}

beforeEach(() => {
  useSitesMock.mockReturnValue({ data: [] });
  useIsMultiSiteMock.mockReturnValue(false);
  apiMocks.getCriteria.mockResolvedValue([]);
  apiMocks.getRequestChildren.mockResolvedValue([]);
  apiMocks.getTemplates.mockResolvedValue([]);
  apiMocks.getSpaces.mockResolvedValue([]);
  apiMocks.createRequest.mockResolvedValue({});
  apiMocks.moveRequest.mockResolvedValue({});
});

describe("RequestFormDialog", () => {
  it("renders the create title and name field", () => {
    renderDialog();
    expect(screen.getByText("Create New Request")).toBeInTheDocument();
    expect(
      screen.getByPlaceholderText(/Product Launch Event/),
    ).toBeInTheDocument();
  });

  it("renders an editable Type select in create mode, defaulting to Task", () => {
    renderDialog();
    const typeTrigger = screen.getByRole("combobox", { name: /type/i });
    expect(typeTrigger).toHaveTextContent("Task");
  });

  it("lets create mode switch the Type select to Group", async () => {
    renderDialog();
    const typeTrigger = screen.getByRole("combobox", { name: /type/i });
    await userEvent.click(typeTrigger);
    await userEvent.click(await screen.findByRole("option", { name: /Group/ }));
    expect(typeTrigger).toHaveTextContent("Group");
  });

  it("renders the child-creation title with the parent name", () => {
    renderDialog({ parentRequest: { id: "p-1", name: "Parent Job" } as Request });
    expect(screen.getByText("Add Child Request")).toBeInTheDocument();
    expect(screen.getByText(/Parent Job/)).toBeInTheDocument();
  });

  it("renders edit mode with the request pre-filled and an Update button", () => {
    renderDialog({ request: EXISTING });
    expect(screen.getByText("Edit Request")).toBeInTheDocument();
    expect(screen.getByDisplayValue("Existing Request")).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Update Request" }),
    ).toBeInTheDocument();
  });

  it("shows a conflict banner listing the request's conflicts", () => {
    renderDialog({
      request: EXISTING,
      conflicts: [
        { id: "c1", kind: "starts_in_off_time", severity: "warning", message: "Off-time during this period" },
        { id: "c2", kind: "overlap", severity: "error", message: "Already assigned" },
      ],
    });
    const banner = screen.getByTestId("conflict-banner");
    expect(banner).toHaveTextContent("2 conflicts on this request");
    expect(banner).toHaveTextContent("Off-time during this period");
  });

  it("shows no conflict banner when the request has no conflicts", () => {
    renderDialog({ request: EXISTING });
    expect(screen.queryByTestId("conflict-banner")).not.toBeInTheDocument();
  });

  it("hides the Site picker for single-site tenants and shows it for multi-site", () => {
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const { rerender } = render(
      <QueryClientProvider client={queryClient}>
        <RequestFormDialog open onOpenChange={vi.fn()} onSave={vi.fn()} />
      </QueryClientProvider>,
    );
    expect(screen.queryByLabelText("Site")).not.toBeInTheDocument();

    useIsMultiSiteMock.mockReturnValue(true);
    useSitesMock.mockReturnValue({ data: [SITE_A, SITE_B] });
    rerender(
      <QueryClientProvider client={queryClient}>
        <RequestFormDialog open onOpenChange={vi.fn()} onSave={vi.fn()} key="ms" />
      </QueryClientProvider>,
    );
    expect(screen.getByText("Site")).toBeInTheDocument();
  });

  it("blocks submit and surfaces an error when the name is empty", async () => {
    const { onSave } = renderDialog();
    const name = screen.getByPlaceholderText(/Product Launch Event/);
    fireEvent.change(name, { target: { value: "   " } });

    fireEvent.click(screen.getByRole("button", { name: "Create Request" }));

    expect(await screen.findByText("Request name is required")).toBeInTheDocument();
    expect(onSave).not.toHaveBeenCalled();
  });

  it("requires a duration of at least 1 for a leaf request", async () => {
    const { onSave } = renderDialog();
    fireEvent.change(screen.getByPlaceholderText(/Product Launch Event/), {
      target: { value: "My Request" },
    });
    // Move to the Timing tab and clear the duration (renders empty for value 0).
    await userEvent.click(screen.getByRole("tab", { name: "Timing" }));
    const duration = screen.getByLabelText(/Minimal Duration/);
    fireEvent.change(duration, { target: { value: "" } });

    // Submit the form directly: clicking the button triggers native `required`
    // validation (the duration input is empty), which would block before the
    // JS validation we want to exercise here.
    fireEvent.submit(duration.closest("form")!);

    expect(
      await screen.findByText("Duration must be at least 1"),
    ).toBeInTheDocument();
    expect(onSave).not.toHaveBeenCalled();
  });

  it("warns when the scheduled window is shorter than the minimal duration", async () => {
    renderDialog();
    await userEvent.click(screen.getByRole("tab", { name: "Timing" }));
    // Default duration is 1 day; seed a 1-hour window -> window < minimal.
    fireEvent.click(screen.getByTestId("seed-window"));

    expect(
      await screen.findByText(/shorter than the minimal duration/),
    ).toBeInTheDocument();
    expect(screen.getByLabelText("timing warning")).toBeInTheDocument();
  });

  it("maps form state to RequestFormData and closes on a successful save", async () => {
    const { onSave, onOpenChange } = renderDialog();
    fireEvent.change(screen.getByPlaceholderText(/Product Launch Event/), {
      target: { value: "  My Request  " },
    });

    fireEvent.click(screen.getByRole("button", { name: "Create Request" }));

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({
        name: "My Request",
        planningMode: "leaf",
        duration: { value: 1, unit: "days" },
      }),
    );
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });

  it("surfaces the error and stays open when save rejects", async () => {
    const onSave = vi.fn(() => Promise.reject(new Error("Server exploded")));
    const onOpenChange = vi.fn();
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <RequestFormDialog open onOpenChange={onOpenChange} onSave={onSave} />
      </QueryClientProvider>,
    );
    fireEvent.change(screen.getByPlaceholderText(/Product Launch Event/), {
      target: { value: "My Request" },
    });

    fireEvent.click(screen.getByRole("button", { name: "Create Request" }));

    expect(await screen.findByText("Server exploded")).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  it("rejects a schedule whose end is before its start", async () => {
    const { onSave } = renderDialog();
    fireEvent.change(screen.getByPlaceholderText(/Product Launch Event/), {
      target: { value: "My Request" },
    });
    await userEvent.click(screen.getByRole("tab", { name: "Timing" }));
    fireEvent.click(screen.getByTestId("seed-bad-order"));

    fireEvent.click(screen.getByRole("button", { name: "Create Request" }));

    expect(
      await screen.findByText(/end.*after.*start|must be after/i),
    ).toBeInTheDocument();
    expect(onSave).not.toHaveBeenCalled();
  });

  it("rejects a start without a matching end", async () => {
    const { onSave } = renderDialog();
    fireEvent.change(screen.getByPlaceholderText(/Product Launch Event/), {
      target: { value: "My Request" },
    });
    await userEvent.click(screen.getByRole("tab", { name: "Timing" }));
    fireEvent.click(screen.getByTestId("seed-start-only"));

    fireEvent.click(screen.getByRole("button", { name: "Create Request" }));

    expect(
      await screen.findByText(/both|together/i),
    ).toBeInTheDocument();
    expect(onSave).not.toHaveBeenCalled();
  });

  it("offers a template selector and applies the chosen template", async () => {
    apiMocks.getTemplates.mockResolvedValue([
      {
        id: "tpl-1",
        name: "Standard Booking",
        durationValue: 3,
        durationUnit: "hours",
      },
    ]);
    const { onSave } = renderDialog();
    await screen.findByText(/Select a template to pre-fill/);
    const templateTrigger = screen
      .getAllByRole("combobox")
      .find((el) => el.textContent?.includes("Select a template"))!;
    await userEvent.click(templateTrigger);
    await userEvent.click(
      await screen.findByRole("option", { name: /Standard Booking/ }),
    );

    // Applying the template pre-fills the duration (3 hours) used on save.
    fireEvent.change(screen.getByPlaceholderText(/Product Launch Event/), {
      target: { value: "From Template" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Create Request" }));
    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({ duration: { value: 3, unit: "hours" } }),
    );
  });

  it("stores the chosen icon in the saved payload", async () => {
    const { onSave } = renderDialog();
    fireEvent.change(screen.getByPlaceholderText(/Product Launch Event/), {
      target: { value: "Iconic" },
    });
    fireEvent.click(screen.getByTestId("pick-icon"));

    fireEvent.click(screen.getByRole("button", { name: "Create Request" }));
    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({ icon: "star" }),
    );
  });

  it("renders the read-only derived schedule for a summary group", async () => {
    renderDialog({
      request: { ...EXISTING, planningMode: "summary" } as Request,
    });
    await userEvent.click(screen.getByRole("tab", { name: "Timing" }));
    expect(
      await screen.findByText(/Derived Schedule/),
    ).toBeInTheDocument();
    // Leaf-only duration field must be absent for a group.
    expect(screen.queryByLabelText(/Minimal Duration/)).not.toBeInTheDocument();
  });

  it("disables the Task option when the edited group has children", async () => {
    apiMocks.getRequestChildren.mockResolvedValue([{ id: "child-1" }]);
    renderDialog({
      request: { ...EXISTING, planningMode: "summary" } as Request,
    });
    expect(
      await screen.findByText(/remove or reassign them/),
    ).toBeInTheDocument();
  });

  it("lets a leaf request pick a space on the Resources tab", async () => {
    apiMocks.getSpaces.mockResolvedValue([
      { id: "space-1", name: "Main Hall" },
    ]);
    renderDialog();
    // Wait for the spaces to load before opening the Resources tab.
    await waitFor(() => expect(apiMocks.getSpaces).toHaveBeenCalled());
    await userEvent.click(screen.getByRole("tab", { name: "Resources" }));
    expect(screen.getByText("Space")).toBeInTheDocument();
  });

  it("toggles scheduling settings and changes the duration unit", async () => {
    renderDialog();
    await userEvent.click(screen.getByRole("tab", { name: "Timing" }));

    const checkbox = screen.getByLabelText(/Apply scheduling settings/);
    await userEvent.click(checkbox);

    // The Resources space select is force-mounted, so target the duration unit
    // trigger by its current value ("Days").
    const unitTrigger = screen
      .getAllByRole("combobox")
      .find((el) => el.textContent?.includes("Days"))!;
    await userEvent.click(unitTrigger);
    await userEvent.click(await screen.findByRole("option", { name: "Hours" }));
    expect(unitTrigger).toHaveTextContent("Hours");
  });

  it("closes via the Cancel button when there are no unsaved changes", () => {
    const { onOpenChange } = renderDialog();
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("marks the form dirty when only a Radix-controlled field changes (Type select)", async () => {
    // Radix Selects don't bubble native input/change events, so dirty tracking
    // happens at the setField layer — changing ONLY the Type then closing must
    // still raise the discard prompt.
    const { onOpenChange } = renderDialog();
    await userEvent.click(screen.getByRole("combobox", { name: "Type" }));
    await userEvent.click(await screen.findByRole("option", { name: /Group/ }));

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(await screen.findByText("Discard changes?")).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  it("marks the form dirty when the icon changes (non-native control)", async () => {
    const { onOpenChange } = renderDialog();
    fireEvent.click(screen.getByTestId("pick-icon"));

    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
    expect(await screen.findByText("Discard changes?")).toBeInTheDocument();
    expect(onOpenChange).not.toHaveBeenCalledWith(false);
  });

  it("shows constraint editing instead of a duration for a boundary container", async () => {
    renderDialog({
      request: { ...EXISTING, planningMode: "container" } as Request,
    });
    await userEvent.click(screen.getByRole("tab", { name: "Timing" }));
    expect(screen.queryByLabelText(/Minimal Duration/)).not.toBeInTheDocument();
  });

  it("edits the description and toggles boundary mode for a group", async () => {
    renderDialog({
      request: { ...EXISTING, planningMode: "summary" } as Request,
    });
    fireEvent.change(screen.getByLabelText("Description"), {
      target: { value: "Some notes" },
    });
    expect(screen.getByDisplayValue("Some notes")).toBeInTheDocument();

    await userEvent.click(screen.getByLabelText(/Boundary mode/));
    // Switching to a boundary container exposes the constraint section on Timing.
    await userEvent.click(screen.getByRole("tab", { name: "Timing" }));
    expect(screen.queryByLabelText(/Minimal Duration/)).not.toBeInTheDocument();
  });

  it("switches an edit-mode group to a Task type", async () => {
    renderDialog({
      request: { ...EXISTING, planningMode: "summary" } as Request,
    });
    const typeTrigger = screen.getByRole("combobox");
    expect(typeTrigger).toHaveTextContent("Group");
    await userEvent.click(typeTrigger);
    await userEvent.click(await screen.findByRole("option", { name: /Task/ }));
    expect(typeTrigger).toHaveTextContent("Task");
  });

  it("saves a group without leaf-only schedule fields", async () => {
    const { onSave } = renderDialog({
      request: { ...EXISTING, planningMode: "summary" } as Request,
    });

    fireEvent.click(screen.getByRole("button", { name: "Update Request" }));

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({
        planningMode: "summary",
        startTs: undefined,
        endTs: undefined,
        resourceId: undefined,
      }),
    );
  });

  it("includes the scheduled window in the saved payload", async () => {
    const { onSave } = renderDialog();
    fireEvent.change(screen.getByPlaceholderText(/Product Launch Event/), {
      target: { value: "My Request" },
    });
    await userEvent.click(screen.getByRole("tab", { name: "Timing" }));
    fireEvent.click(screen.getByTestId("seed-window"));

    fireEvent.click(screen.getByRole("button", { name: "Create Request" }));

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave).toHaveBeenCalledWith(
      expect.objectContaining({
        startTs: expect.stringContaining("2026-01-06"),
        endTs: expect.stringContaining("2026-01-06"),
      }),
    );
  });

  // ── View mode (canEdit=false) ───────────────────────────────────────────────

  it('shows the "Request details" title in view mode', () => {
    renderDialog({ request: EXISTING, canEdit: false });
    expect(screen.getByText("Request details")).toBeInTheDocument();
    expect(screen.getByText("View request details.")).toBeInTheDocument();
  });

  it("disables the editable fields in view mode", () => {
    renderDialog({ request: EXISTING, canEdit: false });
    expect(screen.getByDisplayValue("Existing Request")).toBeDisabled();
    expect(screen.getByLabelText("Description")).toBeDisabled();
  });

  it("renders a Close-only footer in view mode", () => {
    renderDialog({ request: EXISTING, canEdit: false });
    expect(screen.getByTestId("view-close-btn")).toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /Update Request/ }),
    ).not.toBeInTheDocument();
  });

  it("closes when Close is clicked in view mode", () => {
    const { onOpenChange } = renderDialog({ request: EXISTING, canEdit: false });
    fireEvent.click(screen.getByTestId("view-close-btn"));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  // ── Breadcrumb / Children tab / derived rollups (need the tree) ─────────────

  it("renders an ancestor breadcrumb that navigates on click", () => {
    const onNavigate = vi.fn();
    renderDialog({ request: CHILD_1, allRequests: TREE, onNavigate });
    const crumb = screen.getByRole("button", { name: "Group A" });
    fireEvent.click(crumb);
    expect(onNavigate).toHaveBeenCalledWith("grp-1");
  });

  it("lists a group's children and navigates on row click", async () => {
    const onNavigate = vi.fn();
    renderDialog({ request: GROUP, allRequests: TREE, onNavigate });
    await userEvent.click(screen.getByRole("tab", { name: "Children" }));
    expect(screen.getByText("Child One")).toBeInTheDocument();
    expect(screen.getByText("Child Two")).toBeInTheDocument();
    await userEvent.click(screen.getByText("Child One"));
    expect(onNavigate).toHaveBeenCalledWith("c-1");
  });

  it("quick-adds a child via createRequest in edit mode", async () => {
    renderDialog({ request: GROUP, allRequests: TREE });
    await userEvent.click(screen.getByRole("tab", { name: "Children" }));
    fireEvent.change(screen.getByTestId("new-child-name"), {
      target: { value: "Fresh Child" },
    });
    fireEvent.click(screen.getByTestId("add-child-btn"));
    await waitFor(() => expect(apiMocks.createRequest).toHaveBeenCalledTimes(1));
    expect(apiMocks.createRequest).toHaveBeenCalledWith(
      expect.objectContaining({
        parentRequestId: "grp-1",
        name: "Fresh Child",
        planningMode: "leaf",
      }),
    );
  });

  it("removes a child from the group via moveRequest in edit mode", async () => {
    renderDialog({ request: GROUP, allRequests: TREE });
    await userEvent.click(screen.getByRole("tab", { name: "Children" }));
    fireEvent.click(
      screen.getByRole("button", { name: /Remove Child One from group/ }),
    );
    await waitFor(() => expect(apiMocks.moveRequest).toHaveBeenCalledTimes(1));
    expect(apiMocks.moveRequest).toHaveBeenCalledWith(
      "c-1",
      expect.objectContaining({ newParentRequestId: null }),
    );
  });

  it("hides the add/remove child controls in view mode", async () => {
    renderDialog({ request: GROUP, allRequests: TREE, canEdit: false });
    await userEvent.click(screen.getByRole("tab", { name: "Children" }));
    expect(screen.getByText("Child One")).toBeInTheDocument();
    expect(screen.queryByTestId("add-child-btn")).not.toBeInTheDocument();
    expect(
      screen.queryByRole("button", { name: /Remove Child One from group/ }),
    ).not.toBeInTheDocument();
  });

  it("shows the derived sum-of-children rollup for a group with children", async () => {
    renderDialog({ request: GROUP, allRequests: TREE });
    await userEvent.click(screen.getByRole("tab", { name: "Timing" }));
    expect(await screen.findByText(/Derived Schedule/)).toBeInTheDocument();
    expect(screen.getByText("Sum of children")).toBeInTheDocument();
  });

  it("hides the Children tab when the tree is not provided", () => {
    renderDialog({ request: GROUP });
    expect(
      screen.queryByRole("tab", { name: "Children" }),
    ).not.toBeInTheDocument();
  });

  // ── Create-mode Group: Timing placeholder + Children quick-add queue ────────

  it("shows the derived-schedule placeholder on Timing when creating a new group", async () => {
    renderDialog();
    const typeTrigger = screen.getByRole("combobox", { name: /type/i });
    await userEvent.click(typeTrigger);
    await userEvent.click(await screen.findByRole("option", { name: /Group/ }));

    await userEvent.click(screen.getByRole("tab", { name: "Timing" }));

    expect(
      screen.getByText(/automatically calculated from child requests/),
    ).toBeInTheDocument();
  });

  it("hides the Children tab when creating a Task", () => {
    renderDialog();
    expect(
      screen.queryByRole("tab", { name: "Children" }),
    ).not.toBeInTheDocument();
  });

  it("clears queued children when switching create-mode Type back to Task", async () => {
    renderDialog();
    await userEvent.click(screen.getByRole("combobox", { name: /type/i }));
    await userEvent.click(await screen.findByRole("option", { name: /Group/ }));

    await userEvent.click(screen.getByRole("tab", { name: "Children" }));
    fireEvent.change(screen.getByTestId("new-child-name"), {
      target: { value: "Temp Child" },
    });
    fireEvent.click(screen.getByTestId("add-child-btn"));
    expect(screen.getByText("Temp Child")).toBeInTheDocument();

    // The Type select lives on the Details tab, which unmounts its content
    // while another tab is active — switch back to it before changing Type.
    await userEvent.click(screen.getByRole("tab", { name: "Details" }));
    await userEvent.click(screen.getByRole("combobox", { name: /type/i }));
    await userEvent.click(await screen.findByRole("option", { name: /Task/ }));
    await userEvent.click(screen.getByRole("combobox", { name: /type/i }));
    await userEvent.click(await screen.findByRole("option", { name: /Group/ }));

    await userEvent.click(screen.getByRole("tab", { name: "Children" }));
    expect(screen.queryByText("Temp Child")).not.toBeInTheDocument();
    expect(
      screen.getByText(/created together with this group/),
    ).toBeInTheDocument();
  });

  it("queues children in create mode and creates them under the new group on save", async () => {
    // Call counts on shared api mocks persist across tests in this file; start clean.
    apiMocks.createRequest.mockClear();
    const newGroup = { id: "new-group-id", name: "New Group" } as Request;
    const onSave = vi.fn(() => Promise.resolve(newGroup));
    const onOpenChange = vi.fn();
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <RequestFormDialog open onOpenChange={onOpenChange} onSave={onSave} />
      </QueryClientProvider>,
    );
    fireEvent.change(screen.getByPlaceholderText(/Product Launch Event/), {
      target: { value: "New Group" },
    });

    const typeTrigger = screen.getByRole("combobox", { name: /type/i });
    await userEvent.click(typeTrigger);
    await userEvent.click(await screen.findByRole("option", { name: /Group/ }));

    await userEvent.click(screen.getByRole("tab", { name: "Children" }));
    fireEvent.change(screen.getByTestId("new-child-name"), {
      target: { value: "Child A" },
    });
    fireEvent.click(screen.getByTestId("add-child-btn"));
    fireEvent.change(screen.getByTestId("new-child-name"), {
      target: { value: "Child B" },
    });
    fireEvent.click(screen.getByTestId("add-child-btn"));

    expect(screen.getByText("Child A")).toBeInTheDocument();
    expect(screen.getByText("Child B")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Remove Child A" }));
    expect(screen.queryByText("Child A")).not.toBeInTheDocument();
    expect(screen.getByText("Child B")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Create Request" }));

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(apiMocks.createRequest).toHaveBeenCalledTimes(1));
    expect(apiMocks.createRequest).toHaveBeenCalledWith(
      expect.objectContaining({
        parentRequestId: "new-group-id",
        name: "Child B",
        planningMode: "leaf",
      }),
    );
  });

  it("renders the quick-add row above the children list in edit mode", async () => {
    renderDialog({ request: GROUP, allRequests: TREE });
    await userEvent.click(screen.getByRole("tab", { name: "Children" }));
    const quickAdd = screen.getByTestId("new-child-name");
    const firstChildRow = screen.getByText("Child One");
    expect(
      quickAdd.compareDocumentPosition(firstChildRow) &
        Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  // ── Boundary-mode Timing copy (create mode) ─────────────────────────────────

  it("shows the summary placeholder (not the boundary one) on Timing when boundary mode is off", async () => {
    renderDialog();
    const typeTrigger = screen.getByRole("combobox", { name: /type/i });
    await userEvent.click(typeTrigger);
    await userEvent.click(await screen.findByRole("option", { name: /Group/ }));

    await userEvent.click(screen.getByRole("tab", { name: "Timing" }));

    expect(
      screen.getByText(
        "Summary dates and duration are automatically calculated from child requests.",
      ),
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/boundary window below limits/),
    ).not.toBeInTheDocument();
    expect(screen.queryByText("Boundary Window (Optional)")).not.toBeInTheDocument();
  });

  it("shows the boundary placeholder and Boundary Window constraints heading when boundary mode is on", async () => {
    renderDialog();
    const typeTrigger = screen.getByRole("combobox", { name: /type/i });
    await userEvent.click(typeTrigger);
    await userEvent.click(await screen.findByRole("option", { name: /Group/ }));
    await userEvent.click(screen.getByLabelText(/Boundary mode/));

    await userEvent.click(screen.getByRole("tab", { name: "Timing" }));

    expect(
      screen.getByText(
        "Dates and duration roll up from children. The boundary window below limits when they can be scheduled.",
      ),
    ).toBeInTheDocument();
    expect(
      screen.queryByText(/automatically calculated from child requests/),
    ).not.toBeInTheDocument();
    expect(screen.getByText("Boundary Window (Optional)")).toBeInTheDocument();
    expect(
      screen.getByText("Children must start and finish within this window."),
    ).toBeInTheDocument();
  });

  // ── Children tab "Add existing" picker (create mode) ────────────────────────

  it("queues a selected existing candidate in create mode without moving it immediately", async () => {
    apiMocks.moveRequest.mockClear();
    renderDialog({ allRequests: TREE_WITH_CANDIDATES });
    await userEvent.click(screen.getByRole("combobox", { name: /type/i }));
    await userEvent.click(await screen.findByRole("option", { name: /Group/ }));

    await userEvent.click(screen.getByRole("tab", { name: "Children" }));
    fireEvent.click(screen.getByTestId("add-existing-toggle"));

    await screen.findByText("Parentless Task");
    fireEvent.click(screen.getByRole("checkbox", { name: "Parentless Task" }));

    fireEvent.click(screen.getByTestId("add-existing-confirm"));

    expect(
      await screen.findByRole("button", { name: "Remove Parentless Task" }),
    ).toBeInTheDocument();
    expect(apiMocks.moveRequest).not.toHaveBeenCalled();
  });

  it("reparents queued existing requests via moveRequest under the new group on save", async () => {
    apiMocks.moveRequest.mockClear();
    const newGroup = { id: "grp-new", name: "New Group" } as Request;
    const onSave = vi.fn(() => Promise.resolve(newGroup));
    const onOpenChange = vi.fn();
    const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={queryClient}>
        <RequestFormDialog
          open
          onOpenChange={onOpenChange}
          onSave={onSave}
          allRequests={TREE_WITH_CANDIDATES}
        />
      </QueryClientProvider>,
    );
    fireEvent.change(screen.getByPlaceholderText(/Product Launch Event/), {
      target: { value: "New Group" },
    });

    const typeTrigger = screen.getByRole("combobox", { name: /type/i });
    await userEvent.click(typeTrigger);
    await userEvent.click(await screen.findByRole("option", { name: /Group/ }));

    await userEvent.click(screen.getByRole("tab", { name: "Children" }));
    fireEvent.click(screen.getByTestId("add-existing-toggle"));

    await screen.findByText("Parentless Task");
    fireEvent.click(screen.getByRole("checkbox", { name: "Parentless Task" }));
    fireEvent.click(screen.getByTestId("add-existing-confirm"));
    await screen.findByRole("button", { name: "Remove Parentless Task" });

    fireEvent.click(screen.getByRole("button", { name: "Create Request" }));

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(apiMocks.moveRequest).toHaveBeenCalledTimes(1));
    expect(apiMocks.moveRequest).toHaveBeenCalledWith(
      "task-parentless",
      expect.objectContaining({ newParentRequestId: "grp-new" }),
    );
  });

  // ── Children tab "Add existing" picker (edit mode) ──────────────────────────

  it("lists only parentless, non-cyclic requests as Add-existing candidates", async () => {
    renderDialog({ request: GROUP, allRequests: TREE_WITH_CANDIDATES });
    await userEvent.click(screen.getByRole("tab", { name: "Children" }));
    fireEvent.click(screen.getByTestId("add-existing-toggle"));

    const taskCandidate = await screen.findByText("Parentless Task");
    const candidatesPanel = taskCandidate.closest("div.p-1") as HTMLElement;
    expect(within(candidatesPanel).getByText("Parentless Group")).toBeInTheDocument();
    expect(
      within(candidatesPanel).queryByText("Task Under Other Group"),
    ).not.toBeInTheDocument();
    expect(within(candidatesPanel).queryByText("Child One")).not.toBeInTheDocument();
    expect(within(candidatesPanel).queryByText("Group A")).not.toBeInTheDocument();
  });

  it("adds selected candidates via moveRequest and collapses the panel", async () => {
    apiMocks.moveRequest.mockClear();
    renderDialog({ request: GROUP, allRequests: TREE_WITH_CANDIDATES });
    await userEvent.click(screen.getByRole("tab", { name: "Children" }));
    fireEvent.click(screen.getByTestId("add-existing-toggle"));

    await screen.findByText("Parentless Task");
    fireEvent.click(screen.getByRole("checkbox", { name: "Parentless Task" }));

    fireEvent.click(screen.getByTestId("add-existing-confirm"));

    await waitFor(() => expect(apiMocks.moveRequest).toHaveBeenCalledTimes(1));
    expect(apiMocks.moveRequest).toHaveBeenCalledWith(
      "task-parentless",
      expect.objectContaining({ newParentRequestId: "grp-1", sortOrder: expect.any(Number) }),
    );
    await waitFor(() =>
      expect(screen.queryByTestId("add-existing-confirm")).not.toBeInTheDocument(),
    );
  });
});
