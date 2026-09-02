import { describe, it, expect, vi, beforeEach, type Mock } from "vitest";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createFeedbackTestQueryWrapper } from "@foundation/src/test-utils";
import { toast } from "sonner";
import { RequestDependenciesSection } from "./RequestDependenciesSection";
import {
  addRequestDependency,
  deleteRequestDependency,
  getRequestDependencies,
} from "@foundation/src/lib/api/request-dependency-api";
import { updateRequest } from "@foundation/src/lib/api/request-api";
import type { Request } from "@foundation/src/types/requests";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

vi.mock("@foundation/src/lib/api/request-dependency-api", () => ({
  getRequestDependencies: vi.fn(),
  addRequestDependency: vi.fn(),
  deleteRequestDependency: vi.fn(),
}));

vi.mock("@foundation/src/lib/api/request-api", () => ({ updateRequest: vi.fn() }));

const request = { id: "r1", name: "Grind", planningMode: "leaf" } as Request;
const candidates = [
  { id: "r2", name: "Mill", planningMode: "leaf" } as Request,
  { id: "r3", name: "Inspect", planningMode: "leaf" } as Request,
];

function edge(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: "e1",
    predecessorRequestId: "r2",
    successorRequestId: "r1",
    predecessorName: "Mill",
    successorName: "Grind",
    dependencyType: "finish_to_start",
    lagMinutes: 0,
    createdAt: "2026-06-01T00:00:00Z",
    ...overrides,
  };
}

// The production feedback cache, so meta.successMessage / meta.invalidates behave as they do
// at runtime rather than being silently inert in tests.
function renderSection(readOnly = false, allCandidates = candidates, subject: Request = request) {
  const Wrapper = createFeedbackTestQueryWrapper();
  return render(
    <Wrapper>
      <RequestDependenciesSection request={subject} readOnly={readOnly} candidates={allCandidates} />
    </Wrapper>,
  );
}

/** Two predecessors — the point at which the start condition becomes a real choice. */
function twoPredecessors() {
  (getRequestDependencies as Mock).mockResolvedValue({
    predecessors: [edge(), edge({ id: "e2", predecessorRequestId: "r3", predecessorName: "Inspect" })],
    successors: [],
  });
}

beforeEach(() => {
  vi.clearAllMocks();
  (getRequestDependencies as Mock).mockResolvedValue({ predecessors: [], successors: [] });
});

describe("RequestDependenciesSection", () => {
  it("says plainly when a request waits for nothing", async () => {
    renderSection();

    await waitFor(() =>
      expect(screen.getByText(/can start as soon as its own window allows/i)).toBeInTheDocument(),
    );
    expect(screen.getByText(/nothing waits for this request/i)).toBeInTheDocument();
  });

  it("lists both directions separately", async () => {
    (getRequestDependencies as Mock).mockResolvedValue({
      predecessors: [edge()],
      successors: [edge({ id: "e2", predecessorRequestId: "r1", successorRequestId: "r3", successorName: "Inspect" })],
    });

    renderSection();

    // The peer name is what the reader recognises, in each direction.
    await waitFor(() => expect(screen.getByText("Mill")).toBeInTheDocument());
    expect(screen.getByText("Inspect")).toBeInTheDocument();
  });

  it("shows a lag in the largest whole unit", async () => {
    (getRequestDependencies as Mock).mockResolvedValue({
      predecessors: [edge({ lagMinutes: 2880 })],
      successors: [],
    });

    renderSection();

    // 2880 minutes is two days, and saying "2880 min" would make the reader do the arithmetic.
    await waitFor(() => expect(screen.getByText("+2 days")).toBeInTheDocument());
  });

  it("converts the hours the user types into minutes", async () => {
    const user = userEvent.setup();
    (addRequestDependency as Mock).mockResolvedValue(edge());
    renderSection();

    await waitFor(() => expect(screen.getByLabelText(/add something it waits for/i)).toBeInTheDocument());

    await user.click(screen.getByRole("combobox"));
    await user.click(await screen.findByRole("option", { name: /Mill/ }));
    await user.clear(screen.getByLabelText(/gap \(hours\)/i));
    await user.type(screen.getByLabelText(/gap \(hours\)/i), "3");
    await user.click(screen.getByRole("button", { name: "Add" }));

    await waitFor(() => expect(addRequestDependency).toHaveBeenCalledWith("r1", "r2", 180));
    // The shared feedback cache owns the toast; declaring it in meta is what fires it.
    await waitFor(() => expect(toast.success).toHaveBeenCalledWith("Dependency added"));
  });

  it("does not offer a request that is already a predecessor", async () => {
    (getRequestDependencies as Mock).mockResolvedValue({ predecessors: [edge()], successors: [] });
    const user = userEvent.setup();

    renderSection();
    await waitFor(() => expect(screen.getByRole("combobox")).toBeInTheDocument());
    await user.click(screen.getByRole("combobox"));

    // Mill is already linked, so only Inspect remains selectable.
    expect(await screen.findByRole("option", { name: /Inspect/ })).toBeInTheDocument();
    expect(screen.queryByRole("option", { name: /Mill/ })).not.toBeInTheDocument();
  });

  it("removes an edge", async () => {
    (getRequestDependencies as Mock).mockResolvedValue({ predecessors: [edge()], successors: [] });
    (deleteRequestDependency as Mock).mockResolvedValue(undefined);
    const user = userEvent.setup();

    renderSection();
    await user.click(await screen.findByRole("button", { name: /remove dependency on Mill/i }));

    await waitFor(() => expect(deleteRequestDependency).toHaveBeenCalledWith("r1", "e1"));
  });

  it("hides every editing control in read-only mode", async () => {
    (getRequestDependencies as Mock).mockResolvedValue({ predecessors: [edge()], successors: [] });

    renderSection(true);

    await waitFor(() => expect(screen.getByText("Mill")).toBeInTheDocument());
    expect(screen.queryByRole("combobox")).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /remove dependency/i })).not.toBeInTheDocument();
  });

  it("mounts no candidate rows until the picker is opened", async () => {
    // The regression this guards: a plain Select renders every option even while closed, which
    // on a tenant with thousands of requests froze the tab for seconds on each open.
    const many = Array.from({ length: 500 }, (_, i) => ({
      id: `x${i}`,
      name: `Request ${i}`,
      planningMode: "leaf",
    })) as Request[];

    renderSection(false, many);

    await waitFor(() => expect(screen.getByRole("combobox")).toBeInTheDocument());
    expect(screen.queryAllByRole("option")).toHaveLength(0);
  });

  it("caps how many matches it renders and says how many are hidden", async () => {
    const many = Array.from({ length: 500 }, (_, i) => ({
      id: `x${i}`,
      name: `Request ${i}`,
      planningMode: "leaf",
    })) as Request[];
    const user = userEvent.setup();

    renderSection(false, many);
    await waitFor(() => expect(screen.getByRole("combobox")).toBeInTheDocument());
    await user.click(screen.getByRole("combobox"));

    const rendered = await screen.findAllByRole("option");
    expect(rendered.length).toBeLessThanOrEqual(50);
    expect(screen.getByText(/more matches — refine your search/i)).toBeInTheDocument();
  });

  it("treats a blank gap as no gap", async () => {
    const user = userEvent.setup();
    (addRequestDependency as Mock).mockResolvedValue(edge());
    renderSection();

    await waitFor(() => expect(screen.getByRole("combobox")).toBeInTheDocument());
    await user.click(screen.getByRole("combobox"));
    await user.click(await screen.findByRole("option", { name: /Mill/ }));
    await user.clear(screen.getByLabelText(/gap \(hours\)/i));
    await user.click(screen.getByRole("button", { name: "Add" }));

    // Clearing the field must mean "no gap", not NaN — which would serialize to null and come
    // back a 400 with no explanation.
    await waitFor(() => expect(addRequestDependency).toHaveBeenCalledWith("r1", "r2", 0));
  });

  // ── Start condition ─────────────────────────────────────────────────────────

  it("offers no start condition until there is a real choice to make", async () => {
    // With one predecessor every logic means the same thing, so the control would be noise.
    (getRequestDependencies as Mock).mockResolvedValue({ predecessors: [edge()], successors: [] });
    renderSection();

    expect(await screen.findByText("Mill")).toBeInTheDocument();
    expect(screen.queryByLabelText("Can start when")).not.toBeInTheDocument();
  });

  it("offers the start condition once a request waits for several things", async () => {
    twoPredecessors();
    renderSection();

    expect(await screen.findByLabelText("Can start when")).toBeInTheDocument();
  });

  it("sends the chosen logic and clears k when it does not apply", async () => {
    twoPredecessors();
    (updateRequest as Mock).mockResolvedValue({});
    renderSection();

    await userEvent.click(await screen.findByLabelText("Can start when"));
    await userEvent.click(await screen.findByRole("option", { name: "Any predecessor" }));

    // k is meaningless for "any", and a stale one left behind would violate the CHECK.
    await waitFor(() =>
      expect(updateRequest).toHaveBeenCalledWith("r1", {
        predecessorLogic: "any",
        predecessorLogicK: null,
      }),
    );
  });

  it("sends a k with k_of_n, defaulted to every predecessor", async () => {
    twoPredecessors();
    (updateRequest as Mock).mockResolvedValue({});
    renderSection();

    await userEvent.click(await screen.findByLabelText("Can start when"));
    await userEvent.click(await screen.findByRole("option", { name: "At least…" }));

    await waitFor(() =>
      expect(updateRequest).toHaveBeenCalledWith("r1", {
        predecessorLogic: "k_of_n",
        predecessorLogicK: 2,
      }),
    );
  });

  it("shows the k field only for k_of_n, clamped to the predecessor count", async () => {
    twoPredecessors();
    const subject = { ...request, predecessorLogic: "k_of_n", predecessorLogicK: 9 } as Request;
    renderSection(false, candidates, subject);

    // A k that outlived the edges it described still renders as something true: the server
    // clamps it to "all of them", and so does the field.
    const field = await screen.findByLabelText("How many");
    expect(field).toHaveValue(2);
  });

  it("commits k on blur, not on every keystroke", async () => {
    twoPredecessors();
    (updateRequest as Mock).mockResolvedValue({});
    const subject = { ...request, predecessorLogic: "k_of_n", predecessorLogicK: 1 } as Request;
    renderSection(false, candidates, subject);

    const field = await screen.findByLabelText("How many");
    await userEvent.clear(field);
    await userEvent.type(field, "2");

    // Clearing the box used to read as Number("") === 0, clamp to 1 and SAVE — so the field
    // could not be typed into, and every character cost a write plus a tenant-wide invalidation.
    expect(updateRequest).not.toHaveBeenCalled();

    await userEvent.tab();
    await waitFor(() =>
      expect(updateRequest).toHaveBeenCalledExactlyOnceWith("r1", {
        predecessorLogic: "k_of_n",
        predecessorLogicK: 2,
      }),
    );
  });

  it("restores the stored k when the box is left empty", async () => {
    twoPredecessors();
    (updateRequest as Mock).mockResolvedValue({});
    const subject = { ...request, predecessorLogic: "k_of_n", predecessorLogicK: 2 } as Request;
    renderSection(false, candidates, subject);

    const field = await screen.findByLabelText("How many");
    await userEvent.clear(field);
    await userEvent.tab();

    // A half-typed value is not a request to store nothing.
    expect(updateRequest).not.toHaveBeenCalled();
    expect(field).toHaveValue(2);
  });

  it("gives viewers no start-condition control", async () => {
    twoPredecessors();
    renderSection(true);

    expect(await screen.findByLabelText("Can start when")).toBeDisabled();
  });
});
