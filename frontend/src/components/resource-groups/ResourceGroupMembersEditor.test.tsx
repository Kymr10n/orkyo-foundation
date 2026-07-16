/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { ResourceGroupMembersEditor } from "./ResourceGroupMembersEditor";
import type { ResourceInfo, ResourcesResponse } from "@foundation/src/lib/api/resources-api";
import type { ResourceGroupMembersResponse } from "@foundation/src/lib/api/resource-groups-api";

vi.mock("@foundation/src/lib/api/resources-api", () => ({
  getResources: vi.fn(),
}));
vi.mock("@foundation/src/lib/api/resource-groups-api", () => ({
  getResourceGroups: vi.fn(),
  getResourceGroupMembers: vi.fn(),
  setResourceGroupMembers: vi.fn(),
}));
vi.mock("@foundation/src/lib/core/logger", () => ({
  logger: { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() },
}));

import { getResources } from "@foundation/src/lib/api/resources-api";
import {
  getResourceGroups,
  getResourceGroupMembers,
  setResourceGroupMembers,
} from "@foundation/src/lib/api/resource-groups-api";
import { createFeedbackTestQueryWrapper } from "@foundation/src/test-utils";

function makeResource(id: string, name: string, typeKey = "person"): ResourceInfo {
  return {
    id,
    resourceTypeId: `rt-${typeKey}`,
    resourceTypeKey: typeKey,
    name,
    allocationMode: "Fractional",
    baseAvailabilityPercent: 100,
    isActive: true,
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
  };
}

const allResources: ResourceInfo[] = [
  makeResource("r-1", "Alice"),
  makeResource("r-2", "Bob"),
  makeResource("r-3", "Carol"),
];

function makeMembers(ids: string[]): ResourceGroupMembersResponse {
  return {
    groupId: "g-1",
    members: allResources.filter((r) => ids.includes(r.id)),
  };
}

function makeResourcesResponse(items: ResourceInfo[]): ResourcesResponse {
  return { data: items, total: items.length, page: 1, pageSize: 100 };
}

function createWrapper() {
  // Production-identical feedback MutationCache (dialog-feedback.md).
  return createFeedbackTestQueryWrapper();
}

describe("ResourceGroupMembersEditor", () => {
  const defaultProps = {
    open: true,
    onOpenChange: vi.fn(),
    groupId: "g-1",
    groupName: "Alpha Team",
    resourceTypeKey: "person",
    onSuccess: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getResources).mockResolvedValue(makeResourcesResponse(allResources));
    vi.mocked(getResourceGroupMembers).mockResolvedValue(makeMembers(["r-1"]));
    vi.mocked(setResourceGroupMembers).mockResolvedValue(makeMembers(["r-1"]));
  });

  it("renders dialog title with group name", async () => {
    render(<ResourceGroupMembersEditor {...defaultProps} />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText(/Alpha Team/)).toBeInTheDocument();
    });
  });

  it("shows all resources after loading", async () => {
    render(<ResourceGroupMembersEditor {...defaultProps} />, { wrapper: createWrapper() });
    await waitFor(() => expect(screen.getByText("Alice")).toBeInTheDocument());
    expect(screen.getByText("Bob")).toBeInTheDocument();
    expect(screen.getByText("Carol")).toBeInTheDocument();
  });

  it("pre-selects current members", async () => {
    render(<ResourceGroupMembersEditor {...defaultProps} />, { wrapper: createWrapper() });
    await waitFor(() => expect(screen.getByText("Alice")).toBeInTheDocument());
    // Badge shows selected count
    expect(screen.getByText("1 of 3 selected")).toBeInTheDocument();
  });

  it("passes resourceTypeKey to getResources", async () => {
    render(
      <ResourceGroupMembersEditor {...defaultProps} resourceTypeKey="tool" />,
      { wrapper: createWrapper() },
    );
    await waitFor(() =>
      expect(getResources).toHaveBeenCalledWith(
        expect.objectContaining({ resourceTypeKey: "tool" }),
      ),
    );
  });

  it("calls setResourceGroupMembers with selected ids on Save", async () => {
    const user = userEvent.setup();
    render(<ResourceGroupMembersEditor {...defaultProps} />, { wrapper: createWrapper() });
    await waitFor(() => expect(screen.getByText("Bob")).toBeInTheDocument());

    // Toggle Bob (not currently selected)
    const checkboxes = screen.getAllByRole("checkbox");
    // Checkboxes: Select All, Alice (checked), Bob (unchecked), Carol (unchecked)
    await user.click(checkboxes[2]); // Bob

    await user.click(screen.getByRole("button", { name: /Save Changes/i }));
    await waitFor(() =>
      expect(setResourceGroupMembers).toHaveBeenCalledWith(
        "g-1",
        expect.arrayContaining(["r-1", "r-2"]),
      ),
    );
  });

  it("shows empty state when no resources available", async () => {
    vi.mocked(getResources).mockResolvedValue(makeResourcesResponse([]));
    vi.mocked(getResourceGroupMembers).mockResolvedValue(makeMembers([]));
    render(<ResourceGroupMembersEditor {...defaultProps} />, { wrapper: createWrapper() });
    await waitFor(() =>
      expect(screen.getByText(/No resources available/i)).toBeInTheDocument(),
    );
  });

  it("calls onOpenChange(false) when Cancel clicked", async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    render(
      <ResourceGroupMembersEditor {...defaultProps} onOpenChange={onOpenChange} />,
      { wrapper: createWrapper() },
    );
    await waitFor(() => expect(screen.getByText("Alice")).toBeInTheDocument());
    await user.click(screen.getByRole("button", { name: /Cancel/i }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  describe("space 1:1 move-with-confirm", () => {
    const spaces = [makeResource("s-1", "Cell 1", "space")];

    beforeEach(() => {
      // Current group (g-1 = "Group A") is empty; the space already lives in g-2.
      vi.mocked(getResources).mockResolvedValue(makeResourcesResponse(spaces));
      vi.mocked(getResourceGroupMembers).mockImplementation((id: string) =>
        Promise.resolve(
          id === "g-2"
            ? { groupId: "g-2", members: spaces }
            : { groupId: id, members: [] },
        ),
      );
      vi.mocked(getResourceGroups).mockResolvedValue([
        { id: "g-1", name: "Group A", resourceTypeKey: "space", displayOrder: 0 },
        { id: "g-2", name: "Group B", resourceTypeKey: "space", displayOrder: 1 },
      ] as never);
    });

    it("confirms a move before saving, then commits on Move & Save", async () => {
      const user = userEvent.setup();
      const onOpenChange = vi.fn();
      render(
        <ResourceGroupMembersEditor
          {...defaultProps}
          resourceTypeKey="space"
          groupId="g-1"
          groupName="Group A"
          onOpenChange={onOpenChange}
        />,
        { wrapper: createWrapper() },
      );
      await waitFor(() => expect(screen.getByText("Cell 1")).toBeInTheDocument());

      // Select the space that is currently in Group B.
      const checkboxes = screen.getAllByRole("checkbox");
      await user.click(checkboxes[checkboxes.length - 1]);

      // Saving surfaces the move confirmation instead of committing immediately.
      await user.click(screen.getByRole("button", { name: /Save Changes/i }));
      await waitFor(() =>
        expect(screen.getByText(/move from .*Group B.* to .*Group A/i)).toBeInTheDocument(),
      );
      expect(setResourceGroupMembers).not.toHaveBeenCalled();

      // Confirming performs the move.
      await user.click(screen.getByRole("button", { name: /Move & Save/i }));
      await waitFor(() =>
        expect(setResourceGroupMembers).toHaveBeenCalledWith("g-1", ["s-1"]),
      );
    });
  });
});
