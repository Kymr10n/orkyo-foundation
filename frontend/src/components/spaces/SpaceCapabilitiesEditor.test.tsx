/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render as rtlRender, screen, waitFor, type RenderOptions } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { ReactElement } from "react";
import { SpaceCapabilitiesEditor } from "./SpaceCapabilitiesEditor";
import { createFeedbackTestQueryWrapper } from "@foundation/src/test-utils";
import * as criteriaApi from "@foundation/src/lib/api/criteria-api";
import * as spaceCapApi from "@foundation/src/lib/api/space-capability-api";
import type { Criterion } from "@foundation/src/types/criterion";

vi.mock("@foundation/src/lib/api/criteria-api");
vi.mock("@foundation/src/lib/api/space-capability-api");
vi.mock("@foundation/src/lib/core/logger", () => ({
  logger: { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() },
}));

const toastSuccess = vi.fn();
const toastError = vi.fn();
vi.mock("sonner", () => ({ toast: { success: (...a: unknown[]) => toastSuccess(...a), error: (...a: unknown[]) => toastError(...a) } }));

// The editor now saves via useMutation; render under a QueryClientProvider whose
// MutationCache mirrors production so meta-driven toasts/invalidation fire in tests.
const render = (ui: ReactElement, options?: RenderOptions) =>
  rtlRender(ui, { wrapper: createFeedbackTestQueryWrapper(), ...options });

const mockCriteria: Criterion[] = [
  { id: "c1", name: "HasProjector", dataType: "Boolean", resourceTypeKeys: ['space'],
      createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z" },
  { id: "c2", name: "Capacity", dataType: "Number", unit: "seats", resourceTypeKeys: ['space'],
      createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z" },
  { id: "c3", name: "RoomType", dataType: "Enum", enumValues: ["Conference", "Lab"], resourceTypeKeys: ['space'],
      createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z" },
];

const mockExistingCaps = [
  {
    id: "cap-1", resourceId: "space-1", criterionId: "c1", value: true,
    createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z",
    criterion: { id: "c1", name: "HasProjector", dataType: "Boolean" },
  },
];

describe("SpaceCapabilitiesEditor", () => {
  const defaultProps = {
    open: true,
    onOpenChange: vi.fn(),
    siteId: "site-1",
    resourceId: "space-1",
    spaceName: "Room 101",
  };

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(criteriaApi.getCriteria).mockResolvedValue(mockCriteria);
    vi.mocked(spaceCapApi.getSpaceCapabilities).mockResolvedValue(mockExistingCaps);
    vi.mocked(spaceCapApi.addSpaceCapability).mockResolvedValue({
      id: "cap-new", resourceId: "space-1", criterionId: "c2", value: 0,
      createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z",
      criterion: { id: "c2", name: "Capacity", dataType: "Number" },
    });
    vi.mocked(spaceCapApi.deleteSpaceCapability).mockResolvedValue(undefined);
  });

  it("renders dialog title with space name", async () => {
    render(<SpaceCapabilitiesEditor {...defaultProps} />);
    await waitFor(() => {
      expect(screen.getByText(/Room 101/)).toBeInTheDocument();
    });
  });

  it("loads criteria and existing capabilities on open", async () => {
    render(<SpaceCapabilitiesEditor {...defaultProps} />);
    await waitFor(() => {
      expect(criteriaApi.getCriteria).toHaveBeenCalled();
      expect(spaceCapApi.getSpaceCapabilities).toHaveBeenCalledWith("site-1", "space-1");
    });
  });

  it("shows existing capabilities count", async () => {
    render(<SpaceCapabilitiesEditor {...defaultProps} />);
    await waitFor(() => {
      expect(screen.getByText("1 active")).toBeInTheDocument();
    });
  });

  it("does not load data when closed", () => {
    render(<SpaceCapabilitiesEditor {...defaultProps} open={false} />);
    expect(criteriaApi.getCriteria).not.toHaveBeenCalled();
  });

  it("shows error message on load failure", async () => {
    vi.mocked(criteriaApi.getCriteria).mockRejectedValue(new Error("Fetch failed"));
    render(<SpaceCapabilitiesEditor {...defaultProps} />);
    await waitFor(() => {
      expect(screen.getByText("Fetch failed")).toBeInTheDocument();
    });
  });

  it("adds a capability via select + add button", async () => {
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    render(<SpaceCapabilitiesEditor {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText("Select a criterion to add")).toBeInTheDocument();
    });

    await user.click(screen.getByText("Select a criterion to add"));
    await user.click(screen.getByText("Capacity"));

    // Click the Plus icon button
    const buttons = screen.getAllByRole("button");
    const addButton = buttons.find((btn) => btn.querySelector(".lucide-plus"));
    expect(addButton).toBeDefined();
    await user.click(addButton!);

    await waitFor(() => {
      expect(screen.getByText("2 active")).toBeInTheDocument();
    });
  });

  it("saves new capabilities via API", async () => {
    const user = userEvent.setup();
    render(<SpaceCapabilitiesEditor {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText("1 active")).toBeInTheDocument();
    });

    // Save without changes — should check existing caps for diff
    await user.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      expect(spaceCapApi.getSpaceCapabilities).toHaveBeenCalledTimes(2);
    });
  });

  it("closes dialog on successful save", async () => {
    const user = userEvent.setup();
    render(<SpaceCapabilitiesEditor {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText("1 active")).toBeInTheDocument();
    });

    await user.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
    });
    expect(toastSuccess).toHaveBeenCalledWith("Capabilities saved");
  });

  it("shows error on save failure (inline + toast)", async () => {
    vi.mocked(spaceCapApi.getSpaceCapabilities)
      .mockResolvedValueOnce(mockExistingCaps) // initial load
      .mockRejectedValueOnce(new Error("Save error")); // save diff

    const user = userEvent.setup();
    render(<SpaceCapabilitiesEditor {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText("1 active")).toBeInTheDocument();
    });

    await user.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      expect(screen.getByText("Save error")).toBeInTheDocument();
    });
    expect(toastError).toHaveBeenCalledWith(
      "Failed to save capabilities",
      expect.objectContaining({ description: "Save error" }),
    );
  });

  it("parallel-loads criteria and capabilities", async () => {
    render(<SpaceCapabilitiesEditor {...defaultProps} />);
    await waitFor(() => {
      expect(criteriaApi.getCriteria).toHaveBeenCalled();
      expect(spaceCapApi.getSpaceCapabilities).toHaveBeenCalled();
    });
    // Both should be called in the same tick (Promise.all)
    expect(criteriaApi.getCriteria).toHaveBeenCalledTimes(1);
    expect(spaceCapApi.getSpaceCapabilities).toHaveBeenCalledTimes(1);
  });
});
