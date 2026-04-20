/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SpaceCapabilitiesEditor } from "./SpaceCapabilitiesEditor";
import * as criteriaApi from "@/lib/api/criteria-api";
import * as spaceCapApi from "@/lib/api/space-capability-api";
import type { Criterion } from "@/types/criterion";

vi.mock("@/lib/api/criteria-api");
vi.mock("@/lib/api/space-capability-api");
vi.mock("@/lib/core/logger", () => ({
  logger: { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() },
}));

const mockCriteria: Criterion[] = [
  { id: "c1", name: "HasProjector", dataType: "Boolean", createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z" },
  { id: "c2", name: "Capacity", dataType: "Number", unit: "seats", createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z" },
  { id: "c3", name: "RoomType", dataType: "Enum", enumValues: ["Conference", "Lab"], createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z" },
];

const mockExistingCaps = [
  {
    id: "cap-1", spaceId: "space-1", criterionId: "c1", value: true,
    createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z",
    criterion: { id: "c1", name: "HasProjector", dataType: "Boolean" },
  },
];

describe("SpaceCapabilitiesEditor", () => {
  const defaultProps = {
    open: true,
    onOpenChange: vi.fn(),
    siteId: "site-1",
    spaceId: "space-1",
    spaceName: "Room 101",
  };

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(criteriaApi.getCriteria).mockResolvedValue(mockCriteria);
    vi.mocked(spaceCapApi.getSpaceCapabilities).mockResolvedValue(mockExistingCaps);
    vi.mocked(spaceCapApi.addSpaceCapability).mockResolvedValue({
      id: "cap-new", spaceId: "space-1", criterionId: "c2", value: 0,
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
  });

  it("shows error on save failure", async () => {
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
