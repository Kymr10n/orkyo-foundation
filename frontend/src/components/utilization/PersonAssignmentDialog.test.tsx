import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import {
  PersonAssignmentDialog,
  assignmentWindow,
  formatPeriod,
  formatSpan,
  timelineExtent,
} from "./PersonAssignmentDialog";
import { REQUEST_DERIVED_QUERY_KEYS } from "@foundation/src/lib/core/invalidate-request-data";
import type * as ResourceAssignmentsApi from "@foundation/src/lib/api/resource-assignments-api";
import type { PersonAssignmentOption } from "@foundation/src/lib/api/person-candidate-requests-api";
import type { ResourceAssignmentInfo, ValidationResult } from "@foundation/src/lib/api/resource-assignments-api";

vi.mock("sonner", () => ({
  toast: { error: vi.fn(), success: vi.fn() },
}));

vi.mock("@foundation/src/lib/api/person-candidate-requests-api", () => ({
  getPersonAssignmentOptions: vi.fn(),
  mismatchCount: vi.fn((o: PersonAssignmentOption) =>
    o.requirements.filter((r) => !r.satisfied).length,
  ),
  matchesAllRequirements: vi.fn((o: PersonAssignmentOption) =>
    o.requirements.every((r) => r.satisfied),
  ),
}));

vi.mock("@foundation/src/lib/api/resource-assignments-api", async (importActual) => ({
  // Keep the real pure helpers (SOFT_BLOCKER_CODES, hardBlockers, …); mock only network calls.
  ...(await importActual<typeof ResourceAssignmentsApi>()),
  createAssignment: vi.fn(),
  cancelAssignment: vi.fn(),
  validateAssignment: vi.fn(),
  validateAssignmentsBatch: vi.fn(),
}));

import { toast } from "sonner";
import { getPersonAssignmentOptions } from "@foundation/src/lib/api/person-candidate-requests-api";
import {
  createAssignment,
  cancelAssignment,
  validateAssignment,
  validateAssignmentsBatch,
} from "@foundation/src/lib/api/resource-assignments-api";

const START = "2026-01-06T08:00:00Z";
const END = "2026-01-06T10:00:00Z";

const ASSIGNED_OPTION: PersonAssignmentOption = {
  requestId: "req-1",
  name: "Request Alpha",
  startTs: "2026-01-06T08:00:00Z",
  endTs: "2026-01-06T10:00:00Z",
  requirements: [{ label: "CPR", satisfied: true }],
  assignmentId: "asgn-1",
};

const CANDIDATE_OPTION: PersonAssignmentOption = {
  requestId: "req-2",
  name: "Request Beta",
  startTs: "2026-01-06T09:00:00Z",
  endTs: "2026-01-06T11:00:00Z",
  requirements: [{ label: "CPR", satisfied: false }],
  assignmentId: null,
};

const CLEAN_CANDIDATE: PersonAssignmentOption = {
  requestId: "req-3",
  name: "Request Gamma",
  startTs: null,
  endTs: null,
  requirements: [],
  assignmentId: null,
};

const OK_RESULT: ValidationResult = { severity: "ok", blockers: [], warnings: [] };
const BLOCKER_RESULT: ValidationResult = {
  severity: "blocker",
  blockers: [{ code: "offtime.overlap", message: "Overlaps with off-time" }],
  warnings: [],
};
const WARNING_RESULT: ValidationResult = {
  severity: "warning",
  blockers: [],
  warnings: [{ code: "assignment.overbooked", message: "May be overbooked" }],
};
const CAPABILITY_MISSING_RESULT: ValidationResult = {
  severity: "blocker",
  blockers: [{ code: "capability.missing", message: "Resource does not satisfy requirement" }],
  warnings: [],
};
const OVERBOOKED_BLOCKER_RESULT: ValidationResult = {
  severity: "blocker",
  blockers: [
    {
      code: "assignment.overbooked",
      message: "Total allocation (200%) exceeds available capacity (100%)",
    },
  ],
  warnings: [],
};
const MIXED_BLOCKER_RESULT: ValidationResult = {
  severity: "blocker",
  blockers: [
    { code: "capability.missing", message: "Resource does not satisfy requirement" },
    { code: "offtime.overlap", message: "Overlaps with off-time" },
  ],
  warnings: [],
};

const CREATED_ASSIGNMENT: ResourceAssignmentInfo = {
  id: "asgn-new",
  requestId: "req-3",
  resourceId: "person-1",
  resourceTypeKey: "person",
  startUtc: START,
  endUtc: END,
  allocationPercent: 100,
  assignmentStatus: "active",
  createdAt: START,
  updatedAt: START,
};

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

function renderDialog(
  overrides: Partial<React.ComponentProps<typeof PersonAssignmentDialog>> = {},
) {
  return render(
    <PersonAssignmentDialog
      open
      onOpenChange={vi.fn()}
      personId="person-1"
      personName="Alice Smith"
      allocationMode="Exclusive"
      start={START}
      end={END}
      {...overrides}
    />,
    { wrapper },
  );
}

describe("PersonAssignmentDialog", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // Default: assigned rows have no conflicts on load. Tests that assert the conflict
    // indicator override this with a non-empty batch result.
    vi.mocked(validateAssignmentsBatch).mockResolvedValue([]);
  });

  it("shows loading then renders assigned and candidate rows", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([
      ASSIGNED_OPTION,
      CANDIDATE_OPTION,
    ]);
    renderDialog();
    expect(screen.getByText("Loading…")).toBeInTheDocument();
    await waitFor(() => expect(screen.getByText("Request Alpha")).toBeInTheDocument());
    expect(screen.getByText("Request Beta")).toBeInTheDocument();
    const rows = screen.getAllByTestId("assignment-option-row");
    expect(rows).toHaveLength(2);
  });

  it("shows assigned count badge", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([
      ASSIGNED_OPTION,
      CANDIDATE_OPTION,
    ]);
    renderDialog();
    await waitFor(() => expect(screen.getByText("1 assigned")).toBeInTheDocument());
  });

  it("shows mismatch badge for candidate with unsatisfied requirements", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CANDIDATE_OPTION]);
    renderDialog();
    await waitFor(() => expect(screen.getByTestId("mismatch-badge")).toBeInTheDocument());
  });

  it("does not show mismatch badge for fully satisfied candidate", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CLEAN_CANDIDATE]);
    renderDialog();
    await waitFor(() => expect(screen.getByText("Request Gamma")).toBeInTheDocument());
    expect(screen.queryByTestId("mismatch-badge")).not.toBeInTheDocument();
  });

  it("'Eligible only' hides hard-blocked candidates but keeps assignable (incl. soft-blocker) ones", async () => {
    const ELIGIBLE_CANDIDATE: PersonAssignmentOption = {
      requestId: "req-4",
      name: "Request Delta",
      startTs: "2026-01-06T09:00:00Z",
      endTs: "2026-01-06T11:00:00Z",
      requirements: [],
      assignmentId: null,
    };
    const SOFT_ONLY_CANDIDATE: PersonAssignmentOption = {
      requestId: "req-5",
      name: "Request Epsilon",
      startTs: "2026-01-06T09:00:00Z",
      endTs: "2026-01-06T11:00:00Z",
      requirements: [{ label: "CPR", satisfied: false }],
      assignmentId: null,
    };
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([
      CANDIDATE_OPTION, // req-2 — hard blocker → filtered out
      ELIGIBLE_CANDIDATE, // req-4 — clean → kept
      SOFT_ONLY_CANDIDATE, // req-5 — capability missing (soft) → kept
    ]);
    vi.mocked(validateAssignmentsBatch).mockResolvedValue([
      { requestId: "req-2", resourceId: "person-1", result: BLOCKER_RESULT }, // offtime.overlap — hard
      { requestId: "req-4", resourceId: "person-1", result: OK_RESULT },
      { requestId: "req-5", resourceId: "person-1", result: CAPABILITY_MISSING_RESULT }, // soft only — assignable
    ]);
    renderDialog();

    // All three visible before filtering.
    expect(await screen.findByText("Request Beta")).toBeInTheDocument();
    expect(screen.getByText("Request Delta")).toBeInTheDocument();
    expect(screen.getByText("Request Epsilon")).toBeInTheDocument();

    await userEvent.click(screen.getByTestId("eligible-only-toggle"));

    // Hard-blocked one is hidden; clean + soft-blocker ones remain.
    await waitFor(() => expect(screen.queryByText("Request Beta")).not.toBeInTheDocument());
    expect(screen.getByText("Request Delta")).toBeInTheDocument();
    expect(screen.getByText("Request Epsilon")).toBeInTheDocument();

    // Toggling off restores the hard-blocked candidate.
    await userEvent.click(screen.getByTestId("eligible-only-toggle"));
    expect(await screen.findByText("Request Beta")).toBeInTheDocument();
  });

  it("'Eligible only' shows an error note (does not silently fail open) when the check fails", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CANDIDATE_OPTION]);
    vi.mocked(validateAssignmentsBatch).mockRejectedValue(new Error("network"));
    renderDialog();
    await screen.findByText("Request Beta");

    await userEvent.click(screen.getByTestId("eligible-only-toggle"));

    // Fail-open: the candidate is still shown, but the failure is signalled.
    expect(await screen.findByTestId("eligible-check-error")).toBeInTheDocument();
    expect(screen.getByText("Request Beta")).toBeInTheDocument();
  });

  it("surfaces a toast when assigning fails instead of silently reverting", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CLEAN_CANDIDATE]);
    vi.mocked(validateAssignment).mockResolvedValue(OK_RESULT);
    vi.mocked(createAssignment).mockRejectedValue(new Error("boom"));
    renderDialog();

    await userEvent.click(await screen.findByText("Request Gamma"));

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
  });

  it("explains an empty past period instead of the terse 'no requests' message", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([]);
    // end is in the past relative to now → the dialog should say the period has passed.
    renderDialog({ start: "2000-01-01T08:00:00Z", end: "2000-01-01T10:00:00Z" });

    const msg = await screen.findByTestId("no-options-message");
    expect(msg.textContent).toMatch(/already passed/i);
  });

  it("remove: unchecking an assigned row calls cancelAssignment and invalidates cache", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([ASSIGNED_OPTION]);
    vi.mocked(cancelAssignment).mockResolvedValue(undefined);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidateSpy = vi.spyOn(qc, "invalidateQueries");
    render(
      <QueryClientProvider client={qc}>
        <PersonAssignmentDialog
          open
          onOpenChange={vi.fn()}
          personId="person-1"
          personName="Alice"
          allocationMode="Exclusive"
          start={START}
          end={END}
        />
      </QueryClientProvider>,
    );
    await waitFor(() => expect(screen.getByText("Request Alpha")).toBeInTheDocument());
    const checkbox = screen.getByTestId("assignment-checkbox");
    await userEvent.click(checkbox);
    expect(cancelAssignment).toHaveBeenCalledWith("asgn-1");
    // Cancelling an assignment routes through invalidateRequestData — refreshes the full
    // request-derived set (occupancy grids, request lists, conflicts, insights).
    await waitFor(() =>
      expect(invalidateSpy).toHaveBeenCalledWith(
        expect.objectContaining({ queryKey: ["utilization-by-resource"] }),
      ),
    );
    for (const queryKey of REQUEST_DERIVED_QUERY_KEYS) {
      expect(invalidateSpy).toHaveBeenCalledWith(expect.objectContaining({ queryKey }));
    }
  });

  it("add: blocker validation prevents assignment and shows inline issues", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CLEAN_CANDIDATE]);
    vi.mocked(validateAssignment).mockResolvedValue(BLOCKER_RESULT);
    renderDialog();
    await waitFor(() => expect(screen.getByText("Request Gamma")).toBeInTheDocument());
    const checkbox = screen.getByTestId("assignment-checkbox");
    await userEvent.click(checkbox);
    await waitFor(() =>
      expect(screen.getByTestId("item-validation-feedback")).toBeInTheDocument(),
    );
    expect(createAssignment).not.toHaveBeenCalled();
    expect(screen.getByText(/Overlaps with off-time/)).toBeInTheDocument();
  });

  it("add: warning validation still creates the assignment and shows warning", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CLEAN_CANDIDATE]);
    vi.mocked(validateAssignment).mockResolvedValue(WARNING_RESULT);
    vi.mocked(createAssignment).mockResolvedValue(CREATED_ASSIGNMENT);
    renderDialog();
    await waitFor(() => expect(screen.getByText("Request Gamma")).toBeInTheDocument());
    await userEvent.click(screen.getByTestId("assignment-checkbox"));
    expect(createAssignment).toHaveBeenCalledWith(
      expect.objectContaining({ requestId: "req-3", resourceId: "person-1" }),
    );
    await waitFor(() =>
      expect(screen.getByText(/May be overbooked/)).toBeInTheDocument(),
    );
  });

  it("add: ok validation creates assignment with no feedback shown", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CLEAN_CANDIDATE]);
    vi.mocked(validateAssignment).mockResolvedValue(OK_RESULT);
    vi.mocked(createAssignment).mockResolvedValue(CREATED_ASSIGNMENT);
    renderDialog();
    await waitFor(() => expect(screen.getByText("Request Gamma")).toBeInTheDocument());
    await userEvent.click(screen.getByTestId("assignment-checkbox"));
    await waitFor(() => expect(createAssignment).toHaveBeenCalled());
    expect(screen.queryByTestId("item-validation-feedback")).not.toBeInTheDocument();
  });

  it("add: sends no allocation percent for an Exclusive resource", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CLEAN_CANDIDATE]);
    vi.mocked(validateAssignment).mockResolvedValue(OK_RESULT);
    vi.mocked(createAssignment).mockResolvedValue(CREATED_ASSIGNMENT);
    renderDialog({ allocationMode: "Exclusive" });
    await waitFor(() => expect(screen.getByText("Request Gamma")).toBeInTheDocument());
    await userEvent.click(screen.getByTestId("assignment-checkbox"));
    await waitFor(() => expect(createAssignment).toHaveBeenCalled());
    expect(validateAssignment).toHaveBeenCalledWith(
      expect.objectContaining({ allocationPercent: undefined }),
    );
    expect(createAssignment).toHaveBeenCalledWith(
      expect.objectContaining({ allocationPercent: undefined }),
    );
  });

  it("add: sends a full allocation percent for a Fractional resource", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CLEAN_CANDIDATE]);
    vi.mocked(validateAssignment).mockResolvedValue(OK_RESULT);
    vi.mocked(createAssignment).mockResolvedValue(CREATED_ASSIGNMENT);
    renderDialog({ allocationMode: "Fractional" });
    await waitFor(() => expect(screen.getByText("Request Gamma")).toBeInTheDocument());
    await userEvent.click(screen.getByTestId("assignment-checkbox"));
    await waitFor(() => expect(createAssignment).toHaveBeenCalled());
    expect(validateAssignment).toHaveBeenCalledWith(
      expect.objectContaining({ allocationPercent: 100 }),
    );
    expect(createAssignment).toHaveBeenCalledWith(
      expect.objectContaining({ allocationPercent: 100 }),
    );
  });

  it("shows empty state when no options are returned", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([]);
    // Future period → the general "no open requests" empty state (the past-period copy is tested below).
    renderDialog({ start: "2999-01-01T08:00:00Z", end: "2999-01-01T10:00:00Z" });
    await waitFor(() =>
      expect(
        screen.getByText("No open requests overlap this period."),
      ).toBeInTheDocument(),
    );
  });

  it("shows load error when the API call fails", async () => {
    vi.mocked(getPersonAssignmentOptions).mockRejectedValue(new Error("Network error"));
    renderDialog();
    await waitFor(() => expect(screen.getByText("Network error")).toBeInTheDocument());
  });

  it("add: capability.missing blocker creates the assignment (soft override)", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CLEAN_CANDIDATE]);
    vi.mocked(validateAssignment).mockResolvedValue(CAPABILITY_MISSING_RESULT);
    vi.mocked(createAssignment).mockResolvedValue(CREATED_ASSIGNMENT);
    renderDialog();
    await waitFor(() => expect(screen.getByText("Request Gamma")).toBeInTheDocument());
    await userEvent.click(screen.getByTestId("assignment-checkbox"));
    await waitFor(() => expect(createAssignment).toHaveBeenCalled());
  });

  it("add: capability.missing blocker shows warning feedback after creation", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CLEAN_CANDIDATE]);
    vi.mocked(validateAssignment).mockResolvedValue(CAPABILITY_MISSING_RESULT);
    vi.mocked(createAssignment).mockResolvedValue(CREATED_ASSIGNMENT);
    renderDialog();
    await waitFor(() => expect(screen.getByText("Request Gamma")).toBeInTheDocument());
    await userEvent.click(screen.getByTestId("assignment-checkbox"));
    await waitFor(() =>
      expect(screen.getByTestId("item-conflict-feedback")).toBeInTheDocument(),
    );
    expect(screen.getByText(/Resource does not satisfy requirement/)).toBeInTheDocument();
  });

  it("add: assignment.overbooked blocker creates the assignment and warns (soft override)", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CLEAN_CANDIDATE]);
    vi.mocked(validateAssignment).mockResolvedValue(OVERBOOKED_BLOCKER_RESULT);
    vi.mocked(createAssignment).mockResolvedValue(CREATED_ASSIGNMENT);
    renderDialog();
    await waitFor(() => expect(screen.getByText("Request Gamma")).toBeInTheDocument());
    await userEvent.click(screen.getByTestId("assignment-checkbox"));
    await waitFor(() => expect(createAssignment).toHaveBeenCalled());
    await waitFor(() =>
      expect(screen.getByTestId("item-conflict-feedback")).toBeInTheDocument(),
    );
    expect(screen.getByText(/exceeds available capacity/)).toBeInTheDocument();
  });

  it("add: hard blocker (non-capability) still blocks the assignment", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CLEAN_CANDIDATE]);
    vi.mocked(validateAssignment).mockResolvedValue(BLOCKER_RESULT);
    renderDialog();
    await waitFor(() => expect(screen.getByText("Request Gamma")).toBeInTheDocument());
    await userEvent.click(screen.getByTestId("assignment-checkbox"));
    await waitFor(() =>
      expect(screen.getByTestId("item-validation-feedback")).toBeInTheDocument(),
    );
    expect(createAssignment).not.toHaveBeenCalled();
    expect(screen.getByText(/Overlaps with off-time/)).toBeInTheDocument();
  });

  it("add: capability.missing mixed with hard blocker still blocks", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CLEAN_CANDIDATE]);
    vi.mocked(validateAssignment).mockResolvedValue(MIXED_BLOCKER_RESULT);
    renderDialog();
    await waitFor(() => expect(screen.getByText("Request Gamma")).toBeInTheDocument());
    await userEvent.click(screen.getByTestId("assignment-checkbox"));
    await waitFor(() =>
      expect(screen.getByTestId("item-validation-feedback")).toBeInTheDocument(),
    );
    expect(createAssignment).not.toHaveBeenCalled();
  });

  it("add: successful create invalidates all request-derived queries", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CLEAN_CANDIDATE]);
    vi.mocked(validateAssignment).mockResolvedValue(OK_RESULT);
    vi.mocked(createAssignment).mockResolvedValue(CREATED_ASSIGNMENT);
    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    const invalidateSpy = vi.spyOn(qc, "invalidateQueries");
    render(
      <QueryClientProvider client={qc}>
        <PersonAssignmentDialog
          open
          onOpenChange={vi.fn()}
          personId="person-1"
          personName="Alice"
          allocationMode="Exclusive"
          start={START}
          end={END}
        />
      </QueryClientProvider>,
    );
    await waitFor(() => expect(screen.getByText("Request Gamma")).toBeInTheDocument());
    await userEvent.click(screen.getByTestId("assignment-checkbox"));
    await waitFor(() => expect(createAssignment).toHaveBeenCalled());
    // Routed through invalidateRequestData, so an assignment change now refreshes the request lists,
    // conflict badges, and insights charts too — not just the occupancy grids it used to.
    for (const queryKey of REQUEST_DERIVED_QUERY_KEYS) {
      expect(invalidateSpy).toHaveBeenCalledWith(expect.objectContaining({ queryKey }));
    }
  });

  it("filters the list by search input", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([
      ASSIGNED_OPTION,
      CANDIDATE_OPTION,
    ]);
    renderDialog();
    await waitFor(() => expect(screen.getByText("Request Alpha")).toBeInTheDocument());
    const filter = screen.getByTestId("request-filter-input");
    await userEvent.type(filter, "alpha");
    expect(screen.getByText("Request Alpha")).toBeInTheDocument();
    expect(screen.queryByText("Request Beta")).not.toBeInTheDocument();
  });

  it("shows the request duration when the option has a window, and omits it otherwise", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([
      ASSIGNED_OPTION, // 08:00–10:00 → 2h
      CLEAN_CANDIDATE, // null window → no duration
    ]);
    renderDialog();
    await waitFor(() => expect(screen.getByText("Request Alpha")).toBeInTheDocument());

    const durations = screen.getAllByTestId("request-duration");
    expect(durations).toHaveLength(1);
    expect(durations[0]).toHaveTextContent("2h");
  });

  it("renders a timeline bar positioned within the dialog window", async () => {
    // Window 08:00–10:00 (2h); request 09:00–11:00 → starts at 50%, extends past the end
    // so the fill is clamped to a 50% width.
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CANDIDATE_OPTION]);
    renderDialog();
    await waitFor(() => expect(screen.getByText("Request Beta")).toBeInTheDocument());
    const fill = screen.getByTestId("request-timeline-fill");
    expect(fill).toHaveStyle({ marginLeft: "50%", width: "50%" });
  });

  it("clamps a request that starts before the window to the left edge", async () => {
    const early: PersonAssignmentOption = {
      requestId: "req-early",
      name: "Starts Before Window",
      startTs: "2026-01-06T07:00:00Z", // before window start (08:00)
      endTs: "2026-01-06T09:00:00Z",
      requirements: [],
      assignmentId: null,
    };
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([early]);
    renderDialog();
    await waitFor(() => expect(screen.getByText("Starts Before Window")).toBeInTheDocument());
    expect(screen.getByTestId("request-timeline-fill")).toHaveStyle({ marginLeft: "0%" });
  });

  it("renders no timeline bar for an option without a window", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CLEAN_CANDIDATE]);
    renderDialog();
    await waitFor(() => expect(screen.getByText("Request Gamma")).toBeInTheDocument());
    expect(screen.queryByTestId("request-timeline")).not.toBeInTheDocument();
  });

  it("add: assigns over the request's own window, not the clicked segment", async () => {
    // CANDIDATE_OPTION is scheduled 09:00–11:00; the dialog segment is 08:00–10:00.
    // The assignment must use the request window so it doesn't over-allocate the slice.
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([CANDIDATE_OPTION]);
    vi.mocked(validateAssignment).mockResolvedValue(OK_RESULT);
    vi.mocked(createAssignment).mockResolvedValue(CREATED_ASSIGNMENT);
    renderDialog();
    await waitFor(() => expect(screen.getByText("Request Beta")).toBeInTheDocument());
    await userEvent.click(screen.getByTestId("assignment-checkbox"));
    await waitFor(() => expect(createAssignment).toHaveBeenCalled());
    expect(createAssignment).toHaveBeenCalledWith(
      expect.objectContaining({
        startUtc: "2026-01-06T09:00:00Z",
        endUtc: "2026-01-06T11:00:00Z",
      }),
    );
    expect(validateAssignment).toHaveBeenCalledWith(
      expect.objectContaining({
        startUtc: "2026-01-06T09:00:00Z",
        endUtc: "2026-01-06T11:00:00Z",
      }),
    );
  });

  it("shows a conflict indicator on an assigned row that validates as overbooked", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([ASSIGNED_OPTION]);
    vi.mocked(validateAssignmentsBatch).mockResolvedValue([
      {
        requestId: "req-1",
        resourceId: "person-1",
        result: {
          severity: "blocker",
          blockers: [
            {
              code: "assignment.overbooked",
              message: "Total allocation (200%) exceeds available capacity (100%)",
            },
          ],
          warnings: [],
        },
      },
    ]);
    renderDialog();
    await waitFor(() => expect(screen.getByText("Request Alpha")).toBeInTheDocument());

    await waitFor(() => expect(screen.getByTestId("conflict-badge")).toBeInTheDocument());
    expect(screen.getByTestId("conflict-summary-badge")).toHaveTextContent("1 conflicted");
    expect(screen.getByTestId("item-conflict-feedback")).toBeInTheDocument();
    expect(screen.getByText(/exceeds available capacity/)).toBeInTheDocument();
  });

  it("shows no conflict indicator when the assigned row validates clean", async () => {
    vi.mocked(getPersonAssignmentOptions).mockResolvedValue([ASSIGNED_OPTION]);
    vi.mocked(validateAssignmentsBatch).mockResolvedValue([]);
    renderDialog();
    await waitFor(() => expect(screen.getByText("Request Alpha")).toBeInTheDocument());
    // Let any pending conflict validation settle.
    await waitFor(() => expect(validateAssignmentsBatch).toHaveBeenCalled());
    expect(screen.queryByTestId("conflict-badge")).not.toBeInTheDocument();
    expect(screen.queryByTestId("conflict-summary-badge")).not.toBeInTheDocument();
  });
});

describe("PersonAssignmentDialog window/format helpers", () => {
  it("assignmentWindow: uses the request's own window when scheduled", () => {
    expect(assignmentWindow(ASSIGNED_OPTION, START, END)).toEqual({
      startUtc: ASSIGNED_OPTION.startTs,
      endUtc: ASSIGNED_OPTION.endTs,
    });
  });

  it("assignmentWindow: falls back to the clicked segment when the request is unscheduled", () => {
    // CLEAN_CANDIDATE has null startTs/endTs — the segment window must be used so an unscheduled
    // request doesn't over-allocate the resource across the whole visible slice.
    expect(assignmentWindow(CLEAN_CANDIDATE, START, END)).toEqual({
      startUtc: START,
      endUtc: END,
    });
  });

  it("formatPeriod: returns empty string when either endpoint is missing", () => {
    expect(formatPeriod("", END)).toBe("");
    expect(formatPeriod(START, "")).toBe("");
  });

  it("formatPeriod: renders a range for valid endpoints", () => {
    expect(formatPeriod(START, END)).toContain("–");
  });

  it("formatSpan: returns empty string for a non-positive or invalid span", () => {
    expect(formatSpan(END, START)).toBe(""); // end before start
    expect(formatSpan(START, START)).toBe(""); // zero-length
    expect(formatSpan("not-a-date", END)).toBe(""); // unparseable
  });

  it("formatSpan: renders a human duration for a valid span", () => {
    expect(formatSpan(START, END)).toMatch(/h|m/); // 2h between START and END
  });

  it("timelineExtent: returns null for an invalid or zero-length window", () => {
    expect(timelineExtent(END, START, START, END)).toBeNull(); // negative span
    expect(timelineExtent(START, START, START, END)).toBeNull(); // zero span
  });

  it("timelineExtent: keeps interior requests in-range and floors the width", () => {
    const inside = timelineExtent(
      START,
      END,
      "2026-01-06T08:30:00Z",
      "2026-01-06T09:30:00Z",
    );
    expect(inside).not.toBeNull();
    expect(inside!.leftPct).toBeGreaterThanOrEqual(0);
    expect(inside!.leftPct).toBeLessThanOrEqual(100);
    expect(inside!.widthPct).toBeGreaterThanOrEqual(2);
  });

  it("timelineExtent: clamps a request overflowing both edges to the full bar", () => {
    expect(
      timelineExtent(START, END, "2026-01-06T06:00:00Z", "2026-01-06T12:00:00Z"),
    ).toEqual({ leftPct: 0, widthPct: 100 });
  });
});
