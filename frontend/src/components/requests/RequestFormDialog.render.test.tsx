import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
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
}));
vi.mock("@foundation/src/lib/api/criteria-api", () => ({
  getCriteria: apiMocks.getCriteria,
}));
vi.mock("@foundation/src/lib/api/request-api", () => ({
  getRequestChildren: apiMocks.getRequestChildren,
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
  RequestConstraintsSection: () => <div data-testid="constraints" />,
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

function renderDialog(props?: Partial<React.ComponentProps<typeof RequestFormDialog>>) {
  const onSave = vi.fn(() => Promise.resolve());
  const onOpenChange = vi.fn();
  render(
    <RequestFormDialog
      open
      onOpenChange={onOpenChange}
      onSave={onSave}
      {...props}
    />,
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
});

describe("RequestFormDialog", () => {
  it("renders the create title and name field", () => {
    renderDialog();
    expect(screen.getByText("Create New Request")).toBeInTheDocument();
    expect(
      screen.getByPlaceholderText(/Product Launch Event/),
    ).toBeInTheDocument();
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
    const { rerender } = render(
      <RequestFormDialog open onOpenChange={vi.fn()} onSave={vi.fn()} />,
    );
    expect(screen.queryByLabelText("Site")).not.toBeInTheDocument();

    useIsMultiSiteMock.mockReturnValue(true);
    useSitesMock.mockReturnValue({ data: [SITE_A, SITE_B] });
    rerender(
      <RequestFormDialog open onOpenChange={vi.fn()} onSave={vi.fn()} key="ms" />,
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
    render(
      <RequestFormDialog open onOpenChange={onOpenChange} onSave={onSave} />,
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
});
