/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { PeopleGroupMembersEditor } from "./PeopleGroupMembersEditor";
import type { ResourceInfo, ResourcesResponse } from "@foundation/src/lib/api/resources-api";
import type { ResourceGroupMembersResponse } from "@foundation/src/lib/api/resource-groups-api";

// Mirrors GroupSpacesEditor.test — module mocks for the two APIs the editor
// hits, plus a silenced logger. Pattern: mock the modules, then re-import the
// named exports so the spies are referenced by the test bodies.
vi.mock("@foundation/src/lib/api/resources-api", () => ({
  getResources: vi.fn(),
}));
vi.mock("@foundation/src/lib/api/resource-groups-api", () => ({
  getResourceGroupMembers: vi.fn(),
  setResourceGroupMembers: vi.fn(),
}));
vi.mock("@foundation/src/lib/core/logger", () => ({
  logger: { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() },
}));

import { getResources } from "@foundation/src/lib/api/resources-api";
import {
  getResourceGroupMembers,
  setResourceGroupMembers,
} from "@foundation/src/lib/api/resource-groups-api";

function makePerson(id: string, name: string): ResourceInfo {
  return {
    id,
    resourceTypeId: "rt-person",
    resourceTypeKey: "person",
    name,
    allocationMode: "Exclusive",
    baseAvailabilityPercent: 100,
    isActive: true,
    createdAt: "2026-01-01T00:00:00Z",
    updatedAt: "2026-01-01T00:00:00Z",
  };
}

const allPeople: ResourceInfo[] = [
  makePerson("p-1", "Alice"),
  makePerson("p-2", "Bob"),
  makePerson("p-3", "Carol"),
];

function makeMembers(ids: string[]): ResourceGroupMembersResponse {
  return {
    groupId: "g-1",
    members: allPeople.filter((p) => ids.includes(p.id)),
  };
}

function makeResourcesResponse(items: ResourceInfo[]): ResourcesResponse {
  return { data: items, total: items.length, page: 1, pageSize: 100 };
}

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
}

describe("PeopleGroupMembersEditor", () => {
  const defaultProps = {
    open: true,
    onOpenChange: vi.fn(),
    groupId: "g-1",
    groupName: "Top Team",
    onSuccess: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(getResources).mockResolvedValue(makeResourcesResponse(allPeople));
    vi.mocked(getResourceGroupMembers).mockResolvedValue(makeMembers(["p-1"]));
    vi.mocked(setResourceGroupMembers).mockResolvedValue(makeMembers(["p-1"]));
  });

  it("renders dialog title with group name", async () => {
    render(<PeopleGroupMembersEditor {...defaultProps} />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText(/Top Team/)).toBeInTheDocument();
    });
  });

  it("loads people (active only) and current members on open", async () => {
    render(<PeopleGroupMembersEditor {...defaultProps} />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(getResources).toHaveBeenCalledWith({ resourceTypeKey: "person", isActive: true });
      expect(getResourceGroupMembers).toHaveBeenCalledWith("g-1");
    });
  });

  it("pre-selects current members", async () => {
    render(<PeopleGroupMembersEditor {...defaultProps} />, { wrapper: createWrapper() });
    // One member pre-selected out of three people total
    await waitFor(() => {
      expect(screen.getByText("1 of 3 selected")).toBeInTheDocument();
    });
  });

  it("renders every person's name", async () => {
    render(<PeopleGroupMembersEditor {...defaultProps} />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText("Alice")).toBeInTheDocument();
      expect(screen.getByText("Bob")).toBeInTheDocument();
      expect(screen.getByText("Carol")).toBeInTheDocument();
    });
  });

  it("does not load data when closed", () => {
    render(
      <PeopleGroupMembersEditor {...defaultProps} open={false} />,
      { wrapper: createWrapper() },
    );
    expect(getResources).not.toHaveBeenCalled();
    expect(getResourceGroupMembers).not.toHaveBeenCalled();
  });

  it("shows empty state when no people exist", async () => {
    vi.mocked(getResources).mockResolvedValue(makeResourcesResponse([]));
    vi.mocked(getResourceGroupMembers).mockResolvedValue(makeMembers([]));
    render(<PeopleGroupMembersEditor {...defaultProps} />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText("No people available.")).toBeInTheDocument();
    });
  });

  it("shows error on load failure", async () => {
    vi.mocked(getResources).mockRejectedValue(new Error("Network error"));
    render(<PeopleGroupMembersEditor {...defaultProps} />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText("Network error")).toBeInTheDocument();
    });
  });

  it("toggles a person on/off", async () => {
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    render(<PeopleGroupMembersEditor {...defaultProps} />, { wrapper: createWrapper() });

    await waitFor(() => {
      expect(screen.getByText("1 of 3 selected")).toBeInTheDocument();
    });

    // Person checkboxes are rendered in alphabetical order after the
    // "Select All" header checkbox. allPeople = [Alice, Bob, Carol], so:
    //   [0] = Select All, [1] = Alice, [2] = Bob, [3] = Carol
    const checkboxes = screen.getAllByRole("checkbox");
    await user.click(checkboxes[2]); // Bob

    await waitFor(() => {
      expect(screen.getByText("2 of 3 selected")).toBeInTheDocument();
    });
  });

  it("select-all toggles every person on, then off", async () => {
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    render(<PeopleGroupMembersEditor {...defaultProps} />, { wrapper: createWrapper() });

    await waitFor(() => {
      expect(screen.getByText("1 of 3 selected")).toBeInTheDocument();
    });

    // The first checkbox in the document is the header "Select All".
    const selectAll = screen.getAllByRole("checkbox")[0];
    await user.click(selectAll);
    await waitFor(() => expect(screen.getByText("3 of 3 selected")).toBeInTheDocument());

    await user.click(selectAll);
    await waitFor(() => expect(screen.getByText("0 of 3 selected")).toBeInTheDocument());
  });

  it("saves the full id list via setResourceGroupMembers and closes the dialog", async () => {
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    render(<PeopleGroupMembersEditor {...defaultProps} />, { wrapper: createWrapper() });

    await waitFor(() => expect(screen.getByText("1 of 3 selected")).toBeInTheDocument());

    // Add Bob and Carol via Select-All
    const selectAll = screen.getAllByRole("checkbox")[0];
    await user.click(selectAll);
    await waitFor(() => expect(screen.getByText("3 of 3 selected")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      expect(setResourceGroupMembers).toHaveBeenCalledWith(
        "g-1",
        expect.arrayContaining(["p-1", "p-2", "p-3"]),
      );
    });
    expect(defaultProps.onSuccess).toHaveBeenCalled();
    expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
  });

  it("shows error on save failure and stays open", async () => {
    vi.mocked(setResourceGroupMembers).mockRejectedValue(new Error("Update failed"));
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    render(<PeopleGroupMembersEditor {...defaultProps} />, { wrapper: createWrapper() });

    await waitFor(() => expect(screen.getByText("1 of 3 selected")).toBeInTheDocument());

    await user.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      expect(screen.getByText("Update failed")).toBeInTheDocument();
    });
    expect(defaultProps.onOpenChange).not.toHaveBeenCalledWith(false);
  });
});
