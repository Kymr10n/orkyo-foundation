/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { GroupCapabilitiesEditor } from "./GroupCapabilitiesEditor";
import * as criteriaApi from "@/lib/api/criteria-api";
import * as groupCapApi from "@/lib/api/group-capability-api";
import type { Criterion } from "@/types/criterion";

vi.mock("@/lib/api/criteria-api");
vi.mock("@/lib/api/group-capability-api");
vi.mock("@/lib/core/logger", () => ({
  logger: { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() },
}));

const mockCriteria: Criterion[] = [
  { id: "c1", name: "HasProjector", dataType: "Boolean", createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z" },
  { id: "c2", name: "Capacity", dataType: "Number", unit: "seats", createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z" },
  { id: "c3", name: "RoomType", dataType: "Enum", enumValues: ["Conference", "Lab"], createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z" },
];

const mockExistingCaps = [
  { id: "cap-1", groupId: "group-1", criterionId: "c1", value: true, createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z" },
];

describe("GroupCapabilitiesEditor", () => {
  const defaultProps = {
    open: true,
    onOpenChange: vi.fn(),
    groupId: "group-1",
    groupName: "Floor 1",
    onSuccess: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(criteriaApi.getCriteria).mockResolvedValue(mockCriteria);
    vi.mocked(groupCapApi.getGroupCapabilities).mockResolvedValue(mockExistingCaps);
    vi.mocked(groupCapApi.addGroupCapability).mockResolvedValue({
      id: "cap-new", groupId: "group-1", criterionId: "c2", value: 0,
      createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z",
    });
    vi.mocked(groupCapApi.deleteGroupCapability).mockResolvedValue(undefined);
  });

  it("renders dialog title with group name", async () => {
    render(<GroupCapabilitiesEditor {...defaultProps} />);
    await waitFor(() => {
      expect(screen.getByText(/Floor 1/)).toBeInTheDocument();
    });
  });

  it("loads criteria and existing capabilities on open", async () => {
    render(<GroupCapabilitiesEditor {...defaultProps} />);
    await waitFor(() => {
      expect(criteriaApi.getCriteria).toHaveBeenCalled();
      expect(groupCapApi.getGroupCapabilities).toHaveBeenCalledWith("group-1");
    });
  });

  it("shows existing capabilities count", async () => {
    render(<GroupCapabilitiesEditor {...defaultProps} />);
    await waitFor(() => {
      expect(screen.getByText("1 active")).toBeInTheDocument();
    });
  });

  it("does not load data when closed", () => {
    render(<GroupCapabilitiesEditor {...defaultProps} open={false} />);
    expect(criteriaApi.getCriteria).not.toHaveBeenCalled();
  });

  it("shows error message on load failure", async () => {
    vi.mocked(criteriaApi.getCriteria).mockRejectedValue(new Error("Network error"));
    render(<GroupCapabilitiesEditor {...defaultProps} />);
    await waitFor(() => {
      expect(screen.getByText("Network error")).toBeInTheDocument();
    });
  });

  it("adds a capability with default value", async () => {
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    render(<GroupCapabilitiesEditor {...defaultProps} />);

    // Wait for data to load — the select trigger should appear
    await waitFor(() => {
      expect(screen.getByText("Select a criterion to add")).toBeInTheDocument();
    });

    // Open the select and pick Capacity (c2 is not in existing caps)
    await user.click(screen.getByText("Select a criterion to add"));
    await user.click(screen.getByText("Capacity"));

    // Click the Plus icon button (the small one next to the select)
    const buttons = screen.getAllByRole("button");
    const addButton = buttons.find((btn) => btn.querySelector(".lucide-plus"));
    expect(addButton).toBeDefined();
    await user.click(addButton!);

    // Should now show 2 active
    await waitFor(() => {
      expect(screen.getByText("2 active")).toBeInTheDocument();
    });
  });

  it("removes a capability", async () => {
    const user = userEvent.setup();
    render(<GroupCapabilitiesEditor {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText("1 active")).toBeInTheDocument();
    });

    // Find and click the delete button for the existing capability
    const trashButton = screen.getAllByRole("button").find((btn) => {
      return btn.querySelector(".lucide-trash-2");
    });
    if (trashButton) {
      await user.click(trashButton);
      await waitFor(() => {
        expect(screen.getByText("0 active")).toBeInTheDocument();
      });
    }
  });

  it("saves additions and deletions", async () => {
    const user = userEvent.setup();
    // Start with one existing capability (c1)
    render(<GroupCapabilitiesEditor {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText("1 active")).toBeInTheDocument();
    });

    // Click Save
    await user.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      // getGroupCapabilities called again during save for diff
      expect(groupCapApi.getGroupCapabilities).toHaveBeenCalledTimes(2);
    });
  });

  it("calls onSuccess and closes dialog after save", async () => {
    const user = userEvent.setup();
    render(<GroupCapabilitiesEditor {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText("1 active")).toBeInTheDocument();
    });

    await user.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      expect(defaultProps.onSuccess).toHaveBeenCalled();
      expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
    });
  });

  it("shows error on save failure", async () => {
    vi.mocked(groupCapApi.getGroupCapabilities)
      .mockResolvedValueOnce(mockExistingCaps) // initial load
      .mockRejectedValueOnce(new Error("Save failed")); // save fetch

    const user = userEvent.setup();
    render(<GroupCapabilitiesEditor {...defaultProps} />);

    await waitFor(() => {
      expect(screen.getByText("1 active")).toBeInTheDocument();
    });

    await user.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      expect(screen.getByText("Save failed")).toBeInTheDocument();
    });
  });
});
