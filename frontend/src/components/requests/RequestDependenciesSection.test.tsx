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
import type { Request } from "@foundation/src/types/requests";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

vi.mock("@foundation/src/lib/api/request-dependency-api", () => ({
  getRequestDependencies: vi.fn(),
  addRequestDependency: vi.fn(),
  deleteRequestDependency: vi.fn(),
}));

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
function renderSection(readOnly = false, allCandidates = candidates) {
  const Wrapper = createFeedbackTestQueryWrapper();
  return render(
    <Wrapper>
      <RequestDependenciesSection request={request} readOnly={readOnly} candidates={allCandidates} />
    </Wrapper>,
  );
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
});
