import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { PersonAssignmentDialog } from "./PersonAssignmentDialog";
import type { PersonAssignmentOption } from "@foundation/src/lib/api/person-candidate-requests-api";
import type { ResourceAssignmentInfo, ValidationResult } from "@foundation/src/lib/api/resource-assignments-api";

vi.mock("@foundation/src/lib/api/person-candidate-requests-api", () => ({
  getPersonAssignmentOptions: vi.fn(),
  mismatchCount: vi.fn((o: PersonAssignmentOption) =>
    o.requirements.filter((r) => !r.satisfied).length,
  ),
  matchesAllRequirements: vi.fn((o: PersonAssignmentOption) =>
    o.requirements.every((r) => r.satisfied),
  ),
}));

vi.mock("@foundation/src/lib/api/resource-assignments-api", () => ({
  createAssignment: vi.fn(),
  cancelAssignment: vi.fn(),
  validateAssignment: vi.fn(),
}));

import { getPersonAssignmentOptions } from "@foundation/src/lib/api/person-candidate-requests-api";
import {
  createAssignment,
  cancelAssignment,
  validateAssignment,
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
    await waitFor(() =>
      expect(invalidateSpy).toHaveBeenCalledWith(
        expect.objectContaining({ queryKey: ["resource-utilization", "person-1"] }),
      ),
    );
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
    renderDialog();
    await waitFor(() =>
      expect(
        screen.getByText("No active requests overlap this period."),
      ).toBeInTheDocument(),
    );
  });

  it("shows load error when the API call fails", async () => {
    vi.mocked(getPersonAssignmentOptions).mockRejectedValue(new Error("Network error"));
    renderDialog();
    await waitFor(() => expect(screen.getByText("Network error")).toBeInTheDocument());
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
});
