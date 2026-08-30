import { describe, it, expect, vi, beforeEach, type Mock } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ResourceScheduleDialog } from "./ResourceScheduleDialog";

// Capture what the shared calendar is handed, and drive its callbacks — the same approach
// RequestCalendar's own suite uses, so this tests the wiring rather than FullCalendar.
const calendarProps: Record<string, unknown>[] = [];
vi.mock("@foundation/src/components/utilization/RequestCalendar", () => ({
  RequestCalendar: (props: Record<string, unknown>) => {
    calendarProps.push(props);
    return <div data-testid="calendar" />;
  },
}));

vi.mock("@foundation/src/lib/api/resource-assignments-api", () => ({
  getAssignmentsByResource: vi.fn(),
}));
vi.mock("@foundation/src/lib/api/utilization-api", () => ({ scheduleRequest: vi.fn() }));
vi.mock("@foundation/src/lib/api/resource-absences-api", () => ({
  getResourceAbsences: vi.fn(),
  updateResourceAbsence: vi.fn(),
}));
vi.mock("@foundation/src/lib/api/request-api", () => ({ getRequests: vi.fn() }));
vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

const canEdit = vi.fn(() => true);
vi.mock("@foundation/src/hooks/usePermissions", () => ({ useCanEdit: () => canEdit() }));

// The dialogs it can open are stubbed: this suite is about the calendar wiring.
vi.mock("@foundation/src/components/utilization/ResourceAssignmentDialog", () => ({
  ResourceAssignmentDialog: () => <div data-testid="assign-dialog" />,
}));
vi.mock("@foundation/src/components/resources/ResourceAbsenceEditDialog", () => ({
  ResourceAbsenceEditDialog: ({ absence }: { absence?: { id: string } }) => (
    <div data-testid="absence-dialog" data-absence-id={absence?.id ?? "new"} />
  ),
}));

import { getAssignmentsByResource } from "@foundation/src/lib/api/resource-assignments-api";
import { scheduleRequest } from "@foundation/src/lib/api/utilization-api";
import { getResourceAbsences, updateResourceAbsence } from "@foundation/src/lib/api/resource-absences-api";
import { getRequests } from "@foundation/src/lib/api/request-api";
import { toast } from "sonner";

const ASSIGNMENT = {
  id: "a1", requestId: "r1", resourceId: "res1", resourceTypeKey: "machine",
  startUtc: "2026-06-01T09:00:00Z", endUtc: "2026-06-01T17:00:00Z",
  assignmentStatus: "Planned", createdAt: "", updatedAt: "",
};
const ABSENCE = {
  id: "x1", resourceId: "res1", absenceType: "maintenance", title: "Service",
  startTs: "2026-06-02T08:00:00Z", endTs: "2026-06-02T12:00:00Z",
  isRecurring: false, enabled: true, createdAt: "", updatedAt: "",
};

function renderDialog() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={client}>
      <ResourceScheduleDialog
        open
        onOpenChange={() => {}}
        resourceId="res1"
        resourceName="PPF Assembly Bench 1"
        allocationMode="Exclusive"
      />
    </QueryClientProvider>,
  );
}

/** The most recent props the calendar was rendered with. */
const latest = () => calendarProps[calendarProps.length - 1];

beforeEach(() => {
  vi.clearAllMocks();
  calendarProps.length = 0;
  canEdit.mockReturnValue(true);
  (getAssignmentsByResource as Mock).mockResolvedValue([ASSIGNMENT]);
  (getResourceAbsences as Mock).mockResolvedValue([ABSENCE]);
  (getRequests as Mock).mockResolvedValue([{ id: "r1", name: "Mill the bracket" }]);
  (scheduleRequest as Mock).mockResolvedValue({});
  (updateResourceAbsence as Mock).mockResolvedValue(ABSENCE);
});

describe("ResourceScheduleDialog", () => {
  it("shows the resource's bookings and absences on one calendar", async () => {
    renderDialog();

    await waitFor(() => expect((latest().events as unknown[]).length).toBe(2));
    const kinds = (latest().events as { extendedProps: { kind: string } }[]).map(
      (e) => e.extendedProps.kind,
    );
    expect(kinds).toEqual(expect.arrayContaining(["absence", "assignment"]));
    expect(getAssignmentsByResource).toHaveBeenCalledWith("res1", expect.any(Date), expect.any(Date));
  });

  it("reschedules the request behind a dragged booking, not the booking row", async () => {
    // Moving only `resource_assignments` leaves `requests.start_ts` behind, and both the board
    // and the conflict engine read the request — the block springs back and keeps its conflict
    // colour. The work has to move.
    renderDialog();
    await waitFor(() => expect((latest().events as unknown[]).length).toBe(2));

    const move = latest().onEventMove as (id: string, s: Date, e: Date) => void;
    move("a1", new Date("2026-06-03T08:00:00Z"), new Date("2026-06-03T12:00:00Z"));

    await waitFor(() =>
      expect(scheduleRequest).toHaveBeenCalledWith("r1", {
        resourceId: "res1",
        startTs: "2026-06-03T08:00:00.000Z",
        endTs: "2026-06-03T12:00:00.000Z",
      }),
    );
    expect(updateResourceAbsence).not.toHaveBeenCalled();
  });

  it("routes a dragged absence to the absence update, keeping its description", async () => {
    renderDialog();
    await waitFor(() => expect((latest().events as unknown[]).length).toBe(2));

    const resize = latest().onEventResize as (id: string, s: Date, e: Date) => void;
    resize("x1", new Date("2026-06-02T09:00:00Z"), new Date("2026-06-02T15:00:00Z"));

    await waitFor(() =>
      expect(updateResourceAbsence).toHaveBeenCalledWith(
        "res1",
        "x1",
        expect.objectContaining({ absenceType: "maintenance", title: "Service" }),
      ),
    );
    expect(scheduleRequest).not.toHaveBeenCalled();
  });

  it("says so when a move fails rather than leaving the block where it was dropped", async () => {
    (scheduleRequest as Mock).mockRejectedValue(new Error("conflict"));
    renderDialog();
    await waitFor(() => expect((latest().events as unknown[]).length).toBe(2));

    (latest().onEventMove as (id: string, s: Date, e: Date) => void)(
      "a1", new Date("2026-06-03T08:00:00Z"), new Date("2026-06-03T12:00:00Z"),
    );

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
  });

  it("opens an absence for editing when its block is clicked", async () => {
    renderDialog();
    await waitFor(() => expect((latest().events as unknown[]).length).toBe(2));

    (latest().onEventClick as (id: string) => void)("x1");

    await waitFor(() =>
      expect(screen.getByTestId("absence-dialog")).toHaveAttribute("data-absence-id", "x1"),
    );
  });

  it("gives a viewer a read-only calendar", async () => {
    canEdit.mockReturnValue(false);
    renderDialog();

    await waitFor(() => expect(latest().editable).toBe(false));
  });

  it("carries the same legend vocabulary as the board, conflicts included", async () => {
    renderDialog();
    await waitFor(() => expect((latest().events as unknown[]).length).toBe(2));

    const labels = (latest().legend as { label: string }[]).map((l) => l.label);
    expect(labels).toEqual(["Booked", "Absence", "Conflicts", "Warnings"]);
  });

  it("hides the request-status filter, which could only ever hide everything here", async () => {
    renderDialog();
    await waitFor(() => expect(latest().showFilterBar).toBe(false));
  });
});
