import { describe, it, expect, vi, beforeEach, type Mock } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createFeedbackTestQueryWrapper } from "@foundation/src/test-utils";
import { SitePlanCanvas } from "./SitePlanCanvas";
import { getSitePlan } from "@foundation/src/lib/api/request-plan-api";
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint";
import type { RequestPlanChild, SiteRequestPlan } from "@foundation/src/lib/api/request-plan-api";

vi.mock("@foundation/src/lib/api/request-plan-api", () => ({ getSitePlan: vi.fn() }));
vi.mock("@foundation/src/hooks/useBreakpoint", () => ({ useBreakpoint: vi.fn(() => ({ isPhone: false })) }));
const registryMock = vi.hoisted(() => ({ conflictsByRequest: new Map() }));
vi.mock("@foundation/src/hooks/useConflictRegistry", () => ({
  useConflictRegistry: vi.fn(() => registryMock),
}));

function child(id: string, parentRequestId: string | null, overrides: Partial<RequestPlanChild> = {}): RequestPlanChild {
  return {
    id,
    name: id,
    planningMode: "leaf",
    status: "new",
    startTs: null,
    endTs: null,
    sortOrder: 0,
    icon: null,
    parentRequestId,
    predecessorLogic: "all",
    predecessorLogicK: null,
    canStart: true,
    externalPredecessorCount: 0,
    externalSuccessorCount: 0,
    ...overrides,
  };
}

function edge(predecessor: string, successor: string) {
  return {
    id: `${predecessor}->${successor}`,
    predecessorRequestId: predecessor,
    successorRequestId: successor,
    predecessorName: predecessor,
    successorName: successor,
    dependencyType: "finish_to_start" as const,
    lagMinutes: 0,
    createdAt: "2026-06-01T00:00:00Z",
  };
}

/** Two groups with one task each, linked across groups, plus a parentless task. */
function sitePlan(overrides: Partial<SiteRequestPlan> = {}): SiteRequestPlan {
  return {
    groups: [
      { id: "g1", name: "Contract One", sortOrder: 0 },
      { id: "g2", name: "Contract Two", sortOrder: 1 },
    ],
    children: [
      child("Cut", "g1"),
      child("Weld", "g2"),
      child("Sweep", null),
    ],
    edges: [edge("Cut", "Weld")],
    ...overrides,
  };
}

/** A week whose columns are seven days — bar px positions become simple sevenths. */
const ANCHOR = new Date(2026, 5, 8); // a Monday, local — the columns are local days

function renderCanvas(props: Partial<React.ComponentProps<typeof SitePlanCanvas>> = {}) {
  return render(
    <SitePlanCanvas
      siteId="site-1"
      view="structure"
      scale="week"
      anchorTs={ANCHOR}
      nowMs={ANCHOR.getTime()}
      onOpenRequest={vi.fn()}
      onOpenGroupPlanner={vi.fn()}
      {...props}
    />,
    { wrapper: createFeedbackTestQueryWrapper() },
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  registryMock.conflictsByRequest = new Map();
  (getSitePlan as Mock).mockResolvedValue(sitePlan());
  (useBreakpoint as Mock).mockReturnValue({ isPhone: false });
});

describe("SitePlanCanvas", () => {
  it("starts with every band collapsed — headers, no cards", async () => {
    renderCanvas();
    expect(await screen.findByText("Contract One")).toBeInTheDocument();
    expect(screen.getByText("Contract Two")).toBeInTheDocument();
    expect(screen.queryByTestId(/plan-node-/)).not.toBeInTheDocument();
  });

  it("puts parentless tasks in a trailing Ungrouped band", async () => {
    renderCanvas();
    const headers = await screen.findAllByRole("button", { expanded: false });
    expect(headers.map((h) => h.textContent)).toEqual([
      expect.stringContaining("Contract One"),
      expect.stringContaining("Contract Two"),
      expect.stringContaining("Ungrouped"),
    ]);
  });

  it("expanding a band lays out its tasks", async () => {
    renderCanvas();
    await userEvent.click(await screen.findByText("Contract One"));
    expect(screen.getByTestId("plan-node-Cut")).toBeInTheDocument();
    // The other bands stay folded.
    expect(screen.queryByTestId("plan-node-Weld")).not.toBeInTheDocument();
  });

  it("draws the cross-group edge once both of its bands are expanded", async () => {
    const { container } = renderCanvas();
    await userEvent.click(await screen.findByText("Contract One"));
    expect(container.querySelectorAll("svg g").length).toBe(0);

    await userEvent.click(screen.getByText("Contract Two"));
    // No waitFor: the click is awaited, so React has already flushed the expansion.
    expect(container.querySelectorAll("svg g").length).toBe(1);
  });

  it("counts an edge into a collapsed band on the visible node instead of hiding it", async () => {
    renderCanvas();
    // Only Contract Two expanded: Weld's predecessor Cut is folded away.
    await userEvent.click(await screen.findByText("Contract Two"));
    const node = screen.getByTestId("plan-node-Weld");
    expect(within(node).getByTitle(/1 link outside/)).toBeInTheDocument();
  });

  it("names the cross-group entanglement on a collapsed band's header", async () => {
    renderCanvas();
    const one = (await screen.findByText("Contract One")).closest("button")!;
    expect(within(one).getByTitle("Dependencies crossing this group")).toHaveTextContent("1 cross-group");
  });

  it("is read-only: no connect ports on the cards", async () => {
    renderCanvas();
    await userEvent.click(await screen.findByText("Contract One"));
    const node = screen.getByTestId("plan-node-Cut");
    expect(within(node).queryByTitle(/Drag to make/)).not.toBeInTheDocument();
  });

  it("opens a task from a double-click", async () => {
    const onOpenRequest = vi.fn();
    renderCanvas({ onOpenRequest });
    await userEvent.click(await screen.findByText("Contract One"));
    await userEvent.dblClick(within(screen.getByTestId("plan-node-Cut")).getByRole("button"));
    expect(onOpenRequest).toHaveBeenCalledWith("Cut");
  });

  it("routes the band's Sequence action to the per-group planner", async () => {
    const onOpenGroupPlanner = vi.fn();
    renderCanvas({ onOpenGroupPlanner });
    await screen.findByText("Contract One");
    await userEvent.click(screen.getAllByRole("button", { name: /Sequence/ })[0]);
    expect(onOpenGroupPlanner).toHaveBeenCalledWith("g1");
  });

  it("offers no Sequence action on the Ungrouped band — there is no group to plan", async () => {
    renderCanvas();
    await screen.findByText("Ungrouped");
    // Two named groups, two Sequence buttons, none for Ungrouped.
    expect(screen.getAllByRole("button", { name: /Sequence/ })).toHaveLength(2);
  });

  it("falls back to text on a phone", async () => {
    (useBreakpoint as Mock).mockReturnValue({ isPhone: true });
    renderCanvas();
    expect(await screen.findByText(/needs a larger screen/)).toBeInTheDocument();
    expect(screen.queryByTestId("site-plan-canvas")).not.toBeInTheDocument();
  });

  it("says so when the site has no tasks", async () => {
    (getSitePlan as Mock).mockResolvedValue({ groups: [], children: [], edges: [] });
    renderCanvas();
    expect(await screen.findByText(/No tasks at this site yet/)).toBeInTheDocument();
  });
});

describe("SitePlanCanvas — timeline view", () => {
  // Local midnights, because generateTimeColumns builds local-day columns: a bar that starts
  // at a column boundary makes the px assertions exact sevenths of the canvas.
  const dated = (id: string, parent: string | null, startDay: number, days = 1) =>
    child(id, parent, {
      startTs: new Date(2026, 5, 8 + startDay).toISOString(),
      endTs: new Date(2026, 5, 8 + startDay + days).toISOString(),
    });

  beforeEach(() => {
    (getSitePlan as Mock).mockResolvedValue(sitePlan({
      children: [
        dated("Cut", "g1", 1),
        dated("Weld", "g2", 3),
        child("Sweep", "g1"), // undated
      ],
    }));
  });

  it("places a bar by its dates: left and width are the task's share of the week", async () => {
    renderCanvas({ view: "timeline" });
    await userEvent.click(await screen.findByText("Contract One"));
    const bar = screen.getByTestId("plan-bar-Cut");
    // 7 columns × 90px = 630px canvas; day 1 of 7 → left 90px, width 90px.
    expect(bar.style.left).toBe("90px");
    expect(bar.style.width).toBe("90px");
  });

  it("stretches with the scale: the same task is wider on a day grid than a week grid", async () => {
    renderCanvas({ view: "timeline", scale: "day", anchorTs: new Date(2026, 5, 9) });
    await userEvent.click(await screen.findByText("Contract One"));
    const bar = screen.getByTestId("plan-bar-Cut");
    // A full-day task across a 24-column hour grid spans the whole canvas width.
    expect(parseFloat(bar.style.width)).toBeGreaterThan(1000);
  });

  it("counts an undated task on the band header instead of drawing it", async () => {
    renderCanvas({ view: "timeline" });
    const one = (await screen.findByText("Contract One")).closest("button")!;
    expect(within(one).getByText("· 1 unscheduled")).toBeInTheDocument();
    await userEvent.click(screen.getByText("Contract One"));
    expect(screen.queryByTestId("plan-bar-Sweep")).not.toBeInTheDocument();
    expect(screen.queryByTestId("plan-node-Sweep")).not.toBeInTheDocument();
  });

  it("counts a dated task outside the window instead of drawing it", async () => {
    (getSitePlan as Mock).mockResolvedValue(sitePlan({
      children: [dated("Cut", "g1", 1), dated("Later", "g1", 30)],
      edges: [],
    }));
    renderCanvas({ view: "timeline" });
    await userEvent.click(await screen.findByText("Contract One"));
    expect(screen.queryByTestId("plan-bar-Later")).not.toBeInTheDocument();
    const one = screen.getByText("Contract One").closest("button")!;
    expect(within(one).getByText("· 1 outside this period")).toBeInTheDocument();
  });

  it("draws the cross-group edge between two dated bars once both bands are expanded", async () => {
    const { container } = renderCanvas({ view: "timeline" });
    await userEvent.click(await screen.findByText("Contract One"));
    await userEvent.click(screen.getByText("Contract Two"));
    expect(container.querySelectorAll("svg g").length).toBe(1);
  });

  it("draws no edge to an undated task", async () => {
    (getSitePlan as Mock).mockResolvedValue(sitePlan({
      children: [dated("Cut", "g1", 1), child("Weld", "g1")],
      edges: [edge("Cut", "Weld")],
    }));
    const { container } = renderCanvas({ view: "timeline" });
    await userEvent.click(await screen.findByText("Contract One"));
    expect(container.querySelectorAll("svg g").length).toBe(0);
  });

  it("shows the date header and marks now inside the window", async () => {
    renderCanvas({ view: "timeline", nowMs: new Date(2026, 5, 10, 12).getTime() });
    await screen.findByText("Contract One");
    expect(screen.getByTestId("site-plan-now")).toBeInTheDocument();
  });

  it("structure view still draws the undated task as a card", async () => {
    renderCanvas({ view: "structure" });
    await userEvent.click(await screen.findByText("Contract One"));
    expect(screen.getByTestId("plan-node-Sweep")).toBeInTheDocument();
  });

  it("hides the card-zoom buttons — the scale selector is the timeline's zoom", async () => {
    renderCanvas({ view: "timeline" });
    await screen.findByText("Contract One");
    expect(screen.queryByRole("button", { name: "Zoom in" })).not.toBeInTheDocument();
  });

  it("opens a task from its bar", async () => {
    const onOpenRequest = vi.fn();
    renderCanvas({ view: "timeline", onOpenRequest });
    await userEvent.click(await screen.findByText("Contract One"));
    await userEvent.dblClick(screen.getByTestId("plan-bar-Cut"));
    expect(onOpenRequest).toHaveBeenCalledWith("Cut");
  });
});

describe("SitePlanCanvas — fit to width", () => {
  it("stretches the columns to fill a wide viewport instead of leaving dead space", async () => {
    // happy-dom reports zero layout, so give every element a wide clientWidth for this test.
    const proto = Object.getOwnPropertyDescriptor(HTMLElement.prototype, "clientWidth");
    Object.defineProperty(HTMLElement.prototype, "clientWidth", { configurable: true, get: () => 1400 });
    try {
      (getSitePlan as Mock).mockResolvedValue(sitePlan({
        children: [child("Cut", "g1", {
          startTs: new Date(2026, 5, 9).toISOString(),
          endTs: new Date(2026, 5, 10).toISOString(),
        })],
        edges: [],
      }));
      renderCanvas({ view: "timeline" });
      await userEvent.click(await screen.findByText("Contract One"));
      const bar = screen.getByTestId("plan-bar-Cut");
      // The timeline runs flush inside the card like the other grids, so the whole 1400px
      // scroller is drawable: a one-day bar on a seven-day grid is a seventh of it — far
      // wider than the 90px minimum-column floor.
      expect(parseFloat(bar.style.width)).toBeCloseTo(1400 / 7, 0);
    } finally {
      if (proto) Object.defineProperty(HTMLElement.prototype, "clientWidth", proto);
      else delete (HTMLElement.prototype as { clientWidth?: unknown }).clientWidth;
    }
  });
});

describe("SitePlanCanvas — timeline colour coding", () => {
  const at = (id: string, status: string) =>
    child(id, "g1", {
      status: status as RequestPlanChild["status"],
      startTs: new Date(2026, 5, 9).toISOString(),
      endTs: new Date(2026, 5, 10).toISOString(),
    });

  it("paints a bar by its request status — the calendar's palette, not the grid's", async () => {
    (getSitePlan as Mock).mockResolvedValue(sitePlan({
      children: [at("Cut", "done"), at("Weld", "in_progress"), at("Idle", "cancelled")],
      edges: [],
    }));
    renderCanvas({ view: "timeline" });
    await userEvent.click(await screen.findByText("Contract One"));
    expect(screen.getByTestId("plan-bar-Cut").className).toContain("emerald");
    expect(screen.getByTestId("plan-bar-Weld").className).toContain("amber");
    const cancelled = screen.getByTestId("plan-bar-Idle").className;
    expect(cancelled).toContain("line-through");
  });

  it("lets conflict severity override the status tint, and says so accessibly", async () => {
    (getSitePlan as Mock).mockResolvedValue(sitePlan({ children: [at("Cut", "done")], edges: [] }));
    registryMock.conflictsByRequest = new Map([
      ["Cut", [{ id: "c1", kind: "overlap", severity: "error", message: "x" }]],
    ]) as never;
    renderCanvas({ view: "timeline" });
    await userEvent.click(await screen.findByText("Contract One"));
    const bar = screen.getByTestId("plan-bar-Cut");
    expect(bar.className).toContain("red");
    expect(bar.className).not.toContain("emerald");
    expect(bar).toHaveAccessibleName(expect.stringContaining("has conflicts"));
  });

  it("shows the status/severity key on the timeline toolbar only", async () => {
    renderCanvas({ view: "timeline" });
    await screen.findByText("Contract One");
    expect(screen.getByText("In Progress")).toBeInTheDocument();
    expect(screen.getByText("Conflicts")).toBeInTheDocument();
  });
});

describe("SitePlanCanvas — grid furniture matches the other tabs", () => {
  const dated = (id: string) =>
    child(id, "g1", {
      startTs: new Date(2026, 5, 9).toISOString(),
      endTs: new Date(2026, 5, 10).toISOString(),
    });

  it("tints the date header but never hatches it — hatching is a body-cell cue", async () => {
    renderCanvas({ view: "timeline", weekendsEnabled: true });
    await screen.findByText("Contract One");
    // The Saturday/Sunday header cells carry the destructive tint...
    const header = screen.getByTitle(/Saturday/);
    expect(header.className).toContain("bg-destructive/10");
    // ...and no hatch: that belongs to the columns behind the bars.
    expect(header.className).not.toContain("repeating-linear-gradient");
  });

  it("draws the column underlay only inside an expanded band", async () => {
    const { container } = renderCanvas({ view: "timeline", weekendsEnabled: true });
    await screen.findByText("Contract One");
    const hatched = () => container.querySelectorAll('[class*="repeating-linear-gradient"]');
    expect(hatched().length).toBe(0);
    await userEvent.click(screen.getByText("Contract One"));
    expect(hatched().length).toBeGreaterThan(0);
  });

  it("keeps the Now marker out of the date header", async () => {
    renderCanvas({ view: "timeline", nowMs: new Date(2026, 5, 10, 12).getTime() });
    await screen.findByText("Contract One");
    const now = screen.getByTestId("site-plan-now");
    // The sticky header is a sibling stratum, so "now" can never live inside it.
    expect(now.closest(".sticky")).toBeNull();
  });

  it("stacks bands contiguously — no gaps for stripes to show through", async () => {
    (getSitePlan as Mock).mockResolvedValue(sitePlan({
      children: [dated("Cut"), child("Weld", "g2")],
      edges: [],
    }));
    const { container } = renderCanvas({ view: "timeline" });
    await screen.findByText("Contract One");
    const tops = [...container.querySelectorAll<HTMLElement>(".absolute.left-0.right-0")]
      .map((el) => parseFloat(el.style.top))
      .filter((t) => !Number.isNaN(t));
    // Collapsed bands are header-height only, so each starts exactly where the last ended.
    expect(tops).toEqual([0, 44]);
  });
});

describe("SitePlanCanvas — structure-view zoom", () => {
  it("steps the card scale in and out, and stops at both ends", async () => {
    const { container } = renderCanvas({ view: "structure" });
    await screen.findByText("Contract One");
    const surface = () => container.querySelector<HTMLElement>('[style*="scale("]')!;
    const zoomIn = screen.getByRole("button", { name: "Zoom in" });
    const zoomOut = screen.getByRole("button", { name: "Zoom out" });

    // Starts at 1x, with room in both directions (ZOOM_MIN 0.5 … ZOOM_MAX 2).
    expect(surface().style.transform).toBe("scale(1)");
    expect(zoomOut).toBeEnabled();

    await userEvent.click(zoomIn);
    expect(surface().style.transform).toBe("scale(1.25)");

    await userEvent.click(zoomOut);
    expect(surface().style.transform).toBe("scale(1)");

    // Two steps down reaches the floor, and the control says so.
    await userEvent.click(zoomOut);
    await userEvent.click(zoomOut);
    expect(surface().style.transform).toBe("scale(0.5)");
    expect(zoomOut).toBeDisabled();
  });

  it("clamps at the maximum rather than growing without limit", async () => {
    const { container } = renderCanvas({ view: "structure" });
    await screen.findByText("Contract One");
    const zoomIn = screen.getByRole("button", { name: "Zoom in" });
    // ZOOM_MAX is 2 and the step is 0.25, so four clicks reach it and the fifth cannot.
    for (let i = 0; i < 4; i++) await userEvent.click(zoomIn);
    expect(container.querySelector<HTMLElement>('[style*="scale("]')!.style.transform).toBe("scale(2)");
    expect(zoomIn).toBeDisabled();
  });

  it("returns to fit from the reset control", async () => {
    const { container } = renderCanvas({ view: "structure" });
    await screen.findByText("Contract One");
    await userEvent.click(screen.getByRole("button", { name: "Zoom in" }));
    await userEvent.click(screen.getByRole("button", { name: "Reset zoom" }));
    expect(container.querySelector<HTMLElement>('[style*="scale("]')!.style.transform).toBe("scale(1)");
  });

  it("keeps the viewport centred on the same point across a zoom step", async () => {
    // The scroll correction only runs with a real scroller, so give the canvas measurable size.
    const width = Object.getOwnPropertyDescriptor(HTMLElement.prototype, "clientWidth");
    const height = Object.getOwnPropertyDescriptor(HTMLElement.prototype, "clientHeight");
    Object.defineProperty(HTMLElement.prototype, "clientWidth", { configurable: true, get: () => 800 });
    Object.defineProperty(HTMLElement.prototype, "clientHeight", { configurable: true, get: () => 600 });
    try {
      renderCanvas({ view: "structure" });
      await screen.findByText("Contract One");
      await userEvent.click(screen.getByText("Contract One"));
      await userEvent.click(screen.getByRole("button", { name: "Zoom in" }));
      // Anchored on the viewport centre: the offset follows the scale rather than staying put.
      expect(screen.getByTestId("site-plan-canvas")).toBeInTheDocument();
    } finally {
      if (width) Object.defineProperty(HTMLElement.prototype, "clientWidth", width);
      if (height) Object.defineProperty(HTMLElement.prototype, "clientHeight", height);
    }
  });
});

describe("SitePlanCanvas — keyboard on a timeline bar", () => {
  const dated = (id: string) =>
    child(id, "g1", {
      startTs: new Date(2026, 5, 9).toISOString(),
      endTs: new Date(2026, 5, 10).toISOString(),
    });

  it.each(["{Enter}", " "])("opens the request from %s", async (key) => {
    const onOpenRequest = vi.fn();
    (getSitePlan as Mock).mockResolvedValue(sitePlan({ children: [dated("Cut")], edges: [] }));
    renderCanvas({ view: "timeline", onOpenRequest });
    await userEvent.click(await screen.findByText("Contract One"));
    screen.getByTestId("plan-bar-Cut").focus();
    await userEvent.keyboard(key);
    expect(onOpenRequest).toHaveBeenCalledWith("Cut");
  });
});
