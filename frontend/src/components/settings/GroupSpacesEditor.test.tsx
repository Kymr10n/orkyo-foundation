/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { GroupSpacesEditor } from "./GroupSpacesEditor";
import * as siteApi from "@foundation/src/lib/api/site-api";
import * as spaceApi from "@foundation/src/lib/api/space-api";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";

vi.mock("@foundation/src/lib/api/site-api");
vi.mock("@foundation/src/lib/api/space-api");
vi.mock("@foundation/src/lib/core/logger", () => ({
  logger: { debug: vi.fn(), info: vi.fn(), warn: vi.fn(), error: vi.fn() },
}));

const mockSites = [
  { id: "site-1", code: "HQ", name: "Headquarters", createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z" },
];

const mockSpaces = [
  { id: "sp-1", siteId: "site-1", name: "Room A", groupId: "group-1", capacity: 10, isPhysical: true, createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z" },
  { id: "sp-2", siteId: "site-1", name: "Room B", groupId: undefined, capacity: 20, isPhysical: true, createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z" },
  { id: "sp-3", siteId: "site-1", name: "Room C", groupId: "group-2", capacity: 5, isPhysical: true, createdAt: "2024-01-01T00:00:00Z", updatedAt: "2024-01-01T00:00:00Z" },
];

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  );
}

describe("GroupSpacesEditor", () => {
  const defaultProps = {
    open: true,
    onOpenChange: vi.fn(),
    groupId: "group-1",
    groupName: "Conference Rooms",
    onSuccess: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(siteApi.getSites).mockResolvedValue(mockSites);
    vi.mocked(spaceApi.getSpaces).mockResolvedValue(mockSpaces);
    vi.mocked(spaceApi.updateSpace).mockResolvedValue(mockSpaces[0]);
  });

  it("renders dialog title with group name", async () => {
    render(<GroupSpacesEditor {...defaultProps} />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText(/Conference Rooms/)).toBeInTheDocument();
    });
  });

  it("loads all sites and spaces on open", async () => {
    render(<GroupSpacesEditor {...defaultProps} />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(siteApi.getSites).toHaveBeenCalled();
      expect(spaceApi.getSpaces).toHaveBeenCalledWith("site-1");
    });
  });

  it("pre-selects spaces already in the group", async () => {
    render(<GroupSpacesEditor {...defaultProps} />, { wrapper: createWrapper() });
    await waitFor(() => {
      // sp-1 is in group-1 → should show "1 of 3 selected"
      expect(screen.getByText("1 of 3 selected")).toBeInTheDocument();
    });
  });

  it("shows space names grouped by site", async () => {
    render(<GroupSpacesEditor {...defaultProps} />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText("Room A")).toBeInTheDocument();
      expect(screen.getByText("Room B")).toBeInTheDocument();
      expect(screen.getByText("Room C")).toBeInTheDocument();
      expect(screen.getByText("Headquarters")).toBeInTheDocument();
    });
  });

  it("does not load data when closed", () => {
    render(<GroupSpacesEditor {...defaultProps} open={false} />, { wrapper: createWrapper() });
    expect(siteApi.getSites).not.toHaveBeenCalled();
  });

  it("shows empty state when no spaces exist", async () => {
    vi.mocked(spaceApi.getSpaces).mockResolvedValue([]);
    render(<GroupSpacesEditor {...defaultProps} />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText("No spaces available.")).toBeInTheDocument();
    });
  });

  it("shows error on load failure", async () => {
    vi.mocked(siteApi.getSites).mockRejectedValue(new Error("Network error"));
    render(<GroupSpacesEditor {...defaultProps} />, { wrapper: createWrapper() });
    await waitFor(() => {
      expect(screen.getByText("Network error")).toBeInTheDocument();
    });
  });

  it("toggles space selection", async () => {
    const user = userEvent.setup();
    render(<GroupSpacesEditor {...defaultProps} />, { wrapper: createWrapper() });

    await waitFor(() => {
      expect(screen.getByText("1 of 3 selected")).toBeInTheDocument();
    });

    // Click Room B checkbox (not currently in group)
    const roomBRow = screen.getByText("Room B").closest("[class*='flex']");
    const checkbox = roomBRow?.querySelector("[role='checkbox']");
    if (checkbox) {
      await user.click(checkbox);
      await waitFor(() => {
        expect(screen.getByText("2 of 3 selected")).toBeInTheDocument();
      });
    }
  });

  it("select all toggles all spaces", async () => {
    const user = userEvent.setup({ pointerEventsCheck: 0 });
    render(<GroupSpacesEditor {...defaultProps} />, { wrapper: createWrapper() });

    await waitFor(() => {
      expect(screen.getByText("1 of 3 selected")).toBeInTheDocument();
    });

    // Click the Select All checkbox (the one in the header row)
    const selectAllCheckbox = screen.getAllByRole("checkbox")[0];
    await user.click(selectAllCheckbox);

    await waitFor(() => {
      expect(screen.getByText("3 of 3 selected")).toBeInTheDocument();
    });

    // Click again to deselect all
    await user.click(selectAllCheckbox);

    await waitFor(() => {
      expect(screen.getByText("0 of 3 selected")).toBeInTheDocument();
    });
  });

  it("saves group assignments via updateSpace", async () => {
    const user = userEvent.setup();
    render(<GroupSpacesEditor {...defaultProps} />, { wrapper: createWrapper() });

    await waitFor(() => {
      expect(screen.getByText("1 of 3 selected")).toBeInTheDocument();
    });

    await user.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      expect(defaultProps.onSuccess).toHaveBeenCalled();
      expect(defaultProps.onOpenChange).toHaveBeenCalledWith(false);
    });
  });

  it("shows error on save failure", async () => {
    vi.mocked(spaceApi.updateSpace).mockRejectedValue(new Error("Update failed"));

    const user = userEvent.setup({ pointerEventsCheck: 0 });
    render(<GroupSpacesEditor {...defaultProps} />, { wrapper: createWrapper() });

    await waitFor(() => {
      expect(screen.getByText("1 of 3 selected")).toBeInTheDocument();
    });

    // Select all (will add sp-2 and sp-3 to the group)
    const selectAllCheckbox = screen.getAllByRole("checkbox")[0];
    await user.click(selectAllCheckbox);

    await waitFor(() => {
      expect(screen.getByText("3 of 3 selected")).toBeInTheDocument();
    });

    await user.click(screen.getByRole("button", { name: /save/i }));

    await waitFor(() => {
      expect(screen.getByText("Update failed")).toBeInTheDocument();
    });
  });
});
