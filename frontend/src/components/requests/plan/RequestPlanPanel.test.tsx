import { describe, it, expect, vi, beforeEach, type Mock } from "vitest";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { createFeedbackTestQueryWrapper } from "@foundation/src/test-utils";
import { RequestPlanPanel } from "./RequestPlanPanel";
import { getRequestPlan } from "@foundation/src/lib/api/request-plan-api";
import { addRequestDependency } from "@foundation/src/lib/api/request-dependency-api";
import { useCanEdit } from "@foundation/src/hooks/usePermissions";
import { useBreakpoint } from "@foundation/src/hooks/useBreakpoint";
import { useConflictRegistry } from "@foundation/src/hooks/useConflictRegistry";
import type { RequestPlan, RequestPlanChild } from "@foundation/src/lib/api/request-plan-api";
import {
  PLAN_COLUMN_GAP,
  PLAN_NODE_HEIGHT,
  PLAN_NODE_WIDTH,
  PLAN_ROW_GAP,
} from "@foundation/src/domain/plan-layout";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("@foundation/src/lib/api/request-plan-api", () => ({ getRequestPlan: vi.fn() }));
vi.mock("@foundation/src/lib/api/request-dependency-api", () => ({
  addRequestDependency: vi.fn(),
  deleteRequestDependency: vi.fn(),
}));
vi.mock("@foundation/src/hooks/usePermissions", () => ({ useCanEdit: vi.fn(() => true) }));
vi.mock("@foundation/src/hooks/useBreakpoint", () => ({ useBreakpoint: vi.fn(() => ({ isPhone: false })) }));
vi.mock("@foundation/src/hooks/useConflictRegistry", () => ({
  useConflictRegistry: vi.fn(() => ({ conflictsByRequest: new Map() })),
}));

function child(id: string, overrides: Partial<RequestPlanChild> = {}): RequestPlanChild {
  return {
    id,
    name: id,
    planningMode: "leaf",
    status: "new",
    startTs: null,
    endTs: null,
    sortOrder: 0,
    icon: null,
    predecessorLogic: "all",
    predecessorLogicK: null,
    canStart: true,
    externalPredecessorCount: 0,
    externalSuccessorCount: 0,
    ...overrides,
  };
}

function plan(overrides: Partial<RequestPlan> = {}): RequestPlan {
  return {
    parentId: "p1",
    parentName: "Line changeover",
    parentPlanningMode: "summary",
    children: [child("Cut", { sortOrder: 0 }), child("Weld", { sortOrder: 1 })],
    edges: [],
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

/** The default two children, already sequenced — the shape most canvas tests need. */
function linkedPlan(overrides: Partial<RequestPlan> = {}): RequestPlan {
  return plan({ edges: [edge("Cut", "Weld")], ...overrides });
}

// The layout is a pure function of the plan, so the drop coordinates are known: two staged
// children sit in column 0 at rows 0 and 1. The surface's rect is 0,0 in jsdom, and zoom is 1.
const cutCentre = () => ({ clientX: PLAN_NODE_WIDTH / 2, clientY: PLAN_NODE_HEIGHT / 2 });
const weldCentre = () => ({
  clientX: PLAN_NODE_WIDTH / 2,
  clientY: PLAN_NODE_HEIGHT + PLAN_ROW_GAP + PLAN_NODE_HEIGHT / 2,
});

/** Move a task out of the tray onto the canvas, the way a user starts sequencing one. */
async function stage(...names: string[]) {
  for (const name of names) {
    await userEvent.click(await screen.findByTestId(`plan-tray-row-${name}`));
  }
}

/** The ports are pointer affordances, deliberately not buttons and not in the tab order. */
async function outgoingPort(name: string) {
  await screen.findByTestId(`plan-node-${name}`);
  return document.querySelector(
    `[title="Drag to make another task wait for ${name}"]`,
  ) as HTMLElement;
}
async function incomingPort(name: string) {
  await screen.findByTestId(`plan-node-${name}`);
  return document.querySelector(
    `[title="Drag to make ${name} wait for another task"]`,
  ) as HTMLElement;
}

function pointerEvent(type: string, at: { clientX: number; clientY: number }) {
  return new PointerEvent(type, { bubbles: true, ...at });
}

function renderPanel(onOpenRequest?: (id: string) => void) {
  const Wrapper = createFeedbackTestQueryWrapper();
  return render(
    <Wrapper>
      <RequestPlanPanel requestId="p1" onOpenRequest={onOpenRequest} />
    </Wrapper>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  (useCanEdit as Mock).mockReturnValue(true);
  (useBreakpoint as Mock).mockReturnValue({ isPhone: false });
  (useConflictRegistry as Mock).mockReturnValue({ conflictsByRequest: new Map() });
  (getRequestPlan as Mock).mockResolvedValue(plan());
});

describe("RequestPlanPanel", () => {
  it("draws the sequenced tasks on the canvas", async () => {
    (getRequestPlan as Mock).mockResolvedValue(linkedPlan());
    renderPanel();

    expect(await screen.findByTestId("plan-node-Cut")).toBeInTheDocument();
    expect(screen.getByTestId("plan-node-Weld")).toBeInTheDocument();
    // Nothing is left over, so there is no list to show.
    expect(screen.queryByTestId("plan-tray")).not.toBeInTheDocument();
  });

  it("keeps tasks no dependency touches off the canvas, in a list", async () => {
    // The defect this answers: 400 unsequenced cards in one column bury the handful that ARE
    // sequenced, and the head of a chain legitimately stays in that column — so linking a task
    // looked like it did nothing.
    renderPanel();

    expect(await screen.findByTestId("plan-tray-row-Cut")).toBeInTheDocument();
    expect(screen.getByTestId("plan-tray-row-Weld")).toBeInTheDocument();
    expect(screen.queryByTestId("plan-node-Cut")).not.toBeInTheDocument();
    expect(screen.getByText("Unsequenced (2)")).toBeInTheDocument();
  });

  it("says what to do when a group has no children yet", async () => {
    (getRequestPlan as Mock).mockResolvedValue(plan({ children: [] }));
    renderPanel();

    expect(await screen.findByText(/has no children yet/)).toBeInTheDocument();
  });

  it("marks a task that cannot start yet, and says why to a screen reader", async () => {
    (getRequestPlan as Mock).mockResolvedValue(
      plan({ children: [child("Weld", { canStart: false, externalPredecessorCount: 1 })] }),
    );
    renderPanel();

    // The lock icon is a visual cue only; the reason has to reach the accessible name too.
    expect(await screen.findByRole("button", { name: /Cannot start yet/ })).toBeInTheDocument();
  });

  it("shows a non-default start condition on the node", async () => {
    (getRequestPlan as Mock).mockResolvedValue(
      plan({
        children: [
          child("Cut"),
          child("Weld", {
            predecessorLogic: "k_of_n",
            predecessorLogicK: 2,
            externalPredecessorCount: 3,
          }),
        ],
      }),
    );
    renderPanel();

    expect(await screen.findByText("2 OF 3")).toBeInTheDocument();
  });

  it("does not badge the default condition, which would be noise on every node", async () => {
    (getRequestPlan as Mock).mockResolvedValue(
      plan({ children: [child("Weld", { externalPredecessorCount: 2 })] }),
    );
    renderPanel();

    await screen.findByTestId("plan-node-Weld");
    expect(screen.queryByText("ALL")).not.toBeInTheDocument();
  });

  it("counts links that leave the group", async () => {
    (getRequestPlan as Mock).mockResolvedValue(
      plan({
        children: [child("Cut", { externalPredecessorCount: 1, externalSuccessorCount: 2 })],
      }),
    );
    renderPanel();

    expect(await screen.findByTitle("3 links outside this group")).toBeInTheDocument();
  });

  it("warns when the plan contains a loop it cannot order", async () => {
    (getRequestPlan as Mock).mockResolvedValue(
      plan({
        edges: [
          {
            id: "e1", predecessorRequestId: "Cut", successorRequestId: "Weld",
            predecessorName: "Cut", successorName: "Weld",
            dependencyType: "finish_to_start", lagMinutes: 0, createdAt: "2026-06-01T00:00:00Z",
          },
          {
            id: "e2", predecessorRequestId: "Weld", successorRequestId: "Cut",
            predecessorName: "Weld", successorName: "Cut",
            dependencyType: "finish_to_start", lagMinutes: 0, createdAt: "2026-06-01T00:00:00Z",
          },
        ],
      }),
    );
    renderPanel();

    // Rendering nothing would be worse: the user needs to see the loop to remove it.
    expect(await screen.findByText(/depend on each other in a loop/)).toBeInTheDocument();
    expect(screen.getByTestId("plan-node-Cut")).toBeInTheDocument();
  });

  it("gives a viewer no port to draw dependencies from", async () => {
    (useCanEdit as Mock).mockReturnValue(false);
    (getRequestPlan as Mock).mockResolvedValue(linkedPlan());
    renderPanel();

    await screen.findByTestId("plan-node-Cut");
    expect(document.querySelector('[title^="Drag to make"]')).toBeNull();
  });

  it("offers an editor a port on each side of every task", async () => {
    (getRequestPlan as Mock).mockResolvedValue(linkedPlan());
    renderPanel();
    await screen.findByTestId("plan-node-Cut");

    // One port each side, on each of the two tasks: the direction is grabbed, not guessed.
    expect(document.querySelectorAll('[title^="Drag to make another task wait for"]')).toHaveLength(2);
    expect(document.querySelectorAll('[title^="Drag to make"][title*="wait for another task"]')).toHaveLength(2);
  });

  it("creates the dependency when a port is dragged onto another task", async () => {
    (addRequestDependency as Mock).mockResolvedValue({});
    renderPanel();
    await stage("Cut", "Weld");

    const port = await outgoingPort("Cut");

    // Release at the TARGET's coordinates, but dispatch every event at the origin port — which
    // is exactly what touch and pen do, because they implicitly capture the pointer to the
    // element that received pointerdown. Resolving the drop from event.target made this a
    // silent no-op on every touch device; the drop is resolved from coordinates instead.
    fireEvent.pointerDown(port, { clientX: 0, clientY: 0 });
    fireEvent(document, pointerEvent("pointermove", weldCentre()));
    fireEvent(document, pointerEvent("pointerup", weldCentre()));

    // addRequestDependency(successor, predecessor): the target waits for the source.
    await waitFor(() => expect(addRequestDependency).toHaveBeenCalledWith("Weld", "Cut"));
  });

  it("runs the other way when the drag starts from the incoming port", async () => {
    // The bug this prevents: with one port only, a user who drags from the task they want to
    // run SECOND gets the dependency backwards, and that task then sits on the left where
    // predecessors belong. Which side you grab now states which way the work flows.
    (addRequestDependency as Mock).mockResolvedValue({});
    renderPanel();
    await stage("Cut", "Weld");

    const port = await incomingPort("Weld");

    fireEvent.pointerDown(port, { clientX: 0, clientY: 0 });
    fireEvent(document, pointerEvent("pointermove", cutCentre()));
    fireEvent(document, pointerEvent("pointerup", cutCentre()));

    // Same pair, same result as the test above — reached from the other end.
    await waitFor(() => expect(addRequestDependency).toHaveBeenCalledWith("Weld", "Cut"));
  });

  it("redraws the plan after a link is created", async () => {
    // The whole point of the gesture: the target must move into a later column once it has a
    // predecessor. If the plan query is not invalidated by the link, the canvas keeps drawing
    // the old shape and the new dependency is invisible until a reload.
    (getRequestPlan as Mock)
      .mockResolvedValueOnce(plan())
      .mockResolvedValue(linkedPlan());
    (addRequestDependency as Mock).mockResolvedValue({});
    renderPanel();
    await stage("Cut", "Weld");

    const port = await outgoingPort("Cut");
    const target = screen.getByTestId("plan-node-Weld");
    // Both start in column 0, so both sit at the same x.
    expect(screen.getByTestId("plan-node-Cut").style.left)
      .toBe(target.style.left);

    fireEvent.pointerDown(port, { clientX: 0, clientY: 0 });
    fireEvent(document, pointerEvent("pointerup", weldCentre()));

    await waitFor(() => expect(addRequestDependency).toHaveBeenCalled());
    // …and the refetched plan must push the successor right.
    await waitFor(() =>
      expect(screen.getByTestId("plan-node-Weld").style.left)
        .not.toBe(screen.getByTestId("plan-node-Cut").style.left),
    );
  });

  it("sends a phone to the list instead of a canvas it cannot drive", async () => {
    (useBreakpoint as Mock).mockReturnValue({ isPhone: true });
    renderPanel();

    (getRequestPlan as Mock).mockResolvedValue(linkedPlan());

    expect(await screen.findByText(/needs a larger screen/)).toBeInTheDocument();
    expect(screen.queryByTestId("plan-node-Cut")).not.toBeInTheDocument();
    expect(screen.queryByTestId("plan-tray")).not.toBeInTheDocument();
  });

  it("offers a keyboard route to link two tasks, however far apart they are", async () => {
    // The ports are pointer-only by design, so this bar is the accessible equivalent — and the
    // only way to connect two tasks that are never on screen together.
    (addRequestDependency as Mock).mockResolvedValue({});
    renderPanel();

    await userEvent.click(await screen.findByTestId("plan-tray-row-Weld"));
    await userEvent.click(screen.getByLabelText("Wait for"));
    await userEvent.click(await screen.findByRole("option", { name: "Cut" }));

    await waitFor(() => expect(addRequestDependency).toHaveBeenCalledWith("Weld", "Cut"));
  });

  it("keeps the ports out of the tab order", async () => {
    // 2 inert stops per task is 832 on a 416-task plan, interleaved with the cards that do work.
    (getRequestPlan as Mock).mockResolvedValue(linkedPlan());
    renderPanel();
    await screen.findByTestId("plan-node-Cut");

    expect(screen.queryByRole("button", { name: /Make another task wait for/ })).not.toBeInTheDocument();
  });

  it("draws an edge the conflict engine has flagged in the error colour", async () => {
    const violating = {
      id: "e1", predecessorRequestId: "Cut", successorRequestId: "Weld",
      predecessorName: "Cut", successorName: "Weld",
      dependencyType: "finish_to_start", lagMinutes: 0, createdAt: "2026-06-01T00:00:00Z",
    };
    (getRequestPlan as Mock).mockResolvedValue(plan({ edges: [violating] }));
    (useConflictRegistry as Mock).mockReturnValue({
      conflictsByRequest: new Map([
        ["Weld", [{ id: "c1", kind: "dependency_violation", severity: "error", message: "x", peerRequestId: "Cut" }]],
      ]),
    });
    renderPanel();

    await screen.findByTestId("plan-node-Weld");
    // Without this the graph draws a tidy plan over work the server has already flagged.
    expect(document.querySelector(".stroke-destructive")).not.toBeNull();
  });

  it("puts a task on the canvas when it is picked from the list", async () => {
    // The gesture the whole rework exists for: the task visibly leaves the list and lands on the
    // plan, so "I linked something and nothing moved" cannot happen again.
    renderPanel();
    await stage("Cut");

    expect(await screen.findByTestId("plan-node-Cut")).toBeInTheDocument();
    expect(screen.queryByTestId("plan-tray-row-Cut")).not.toBeInTheDocument();
    expect(screen.getByText("Unsequenced (1)")).toBeInTheDocument();
  });

  it("moves a task out of the list for good once it is linked", async () => {
    (getRequestPlan as Mock)
      .mockResolvedValueOnce(plan())
      .mockResolvedValue(linkedPlan());
    (addRequestDependency as Mock).mockResolvedValue({});
    renderPanel();

    // Only the successor is staged; the predecessor is chosen from the list on the selection bar.
    await userEvent.click(await screen.findByTestId("plan-tray-row-Weld"));
    await userEvent.click(screen.getByLabelText("Wait for"));
    await userEvent.click(await screen.findByRole("option", { name: "Cut" }));

    // Both ends are sequenced now, so the list empties and disappears.
    await waitFor(() => expect(screen.getByTestId("plan-node-Cut")).toBeInTheDocument());
    await waitFor(() => expect(screen.queryByTestId("plan-tray")).not.toBeInTheDocument());
  });

  it("rings the pair a link was just drawn between", async () => {
    // A link relayers the graph and several cards move at once. Without a mark on the two that
    // changed, the user has to diff the picture in their head to see what happened.
    (getRequestPlan as Mock)
      .mockResolvedValueOnce(plan())
      .mockResolvedValue(linkedPlan());
    (addRequestDependency as Mock).mockResolvedValue({});
    renderPanel();

    await userEvent.click(await screen.findByTestId("plan-tray-row-Weld"));
    await userEvent.click(screen.getByLabelText("Wait for"));
    await userEvent.click(await screen.findByRole("option", { name: "Cut" }));

    await waitFor(() =>
      expect(
        screen.getByTestId("plan-node-Cut").querySelector(".ring-primary\\/60"),
      ).not.toBeNull(),
    );
  });

  it("opens a task on a double-click", async () => {
    const onOpen = vi.fn();
    (getRequestPlan as Mock).mockResolvedValue(linkedPlan());
    renderPanel(onOpen);

    await userEvent.dblClick((await screen.findByTestId("plan-node-Cut")).querySelector("button")!);

    expect(onOpen).toHaveBeenCalledWith("Cut");
  });

  it("leaves a task from the list one press from being opened", async () => {
    // The row leaves the list on the first click, so it has no double-click of its own; the
    // selection bar it just populated is the way in.
    const onOpen = vi.fn();
    renderPanel(onOpen);
    await stage("Weld");

    await userEvent.click(screen.getByRole("button", { name: "Open task" }));

    expect(onOpen).toHaveBeenCalledWith("Weld");
  });

  it("selects a task when its card is clicked", async () => {
    (getRequestPlan as Mock).mockResolvedValue(linkedPlan());
    renderPanel();

    await userEvent.click((await screen.findByTestId("plan-node-Weld")).querySelector("button")!);

    // The selection bar is the keyboard route to everything the ports do by pointer.
    expect(screen.getByRole("button", { name: "Open task" })).toBeInTheDocument();
  });

  it("filters the list, because 400 identically named tasks are not scannable", async () => {
    (getRequestPlan as Mock).mockResolvedValue(
      plan({ children: [child("Cut", { sortOrder: 0 }), child("Weld", { sortOrder: 1 })] }),
    );
    renderPanel();

    await userEvent.type(await screen.findByTestId("plan-tray-filter"), "wel");

    expect(screen.getByTestId("plan-tray-row-Weld")).toBeInTheDocument();
    expect(screen.queryByTestId("plan-tray-row-Cut")).not.toBeInTheDocument();
  });

  it("says what to do when nothing is sequenced yet", async () => {
    renderPanel();

    expect(await screen.findByText(/Nothing is sequenced yet/)).toBeInTheDocument();
  });

  it("puts each task's schedule on its card, since names in one group repeat", async () => {
    (getRequestPlan as Mock).mockResolvedValue(
      linkedPlan({
        children: [
          child("Cut", { sortOrder: 0, startTs: "2026-04-02T08:00:00", endTs: "2026-04-06T17:00:00" }),
          child("Weld", { sortOrder: 1 }),
        ],
      }),
    );
    renderPanel();

    expect(await screen.findByText(/· 5d$/)).toBeInTheDocument();
    // The unscheduled one says so rather than showing an empty line.
    expect(screen.getAllByText("Unscheduled").length).toBeGreaterThan(0);
  });

  it("reports a failure to load rather than an empty canvas", async () => {
    (getRequestPlan as Mock).mockRejectedValue(new Error("nope"));
    renderPanel();

    expect(await screen.findByText(/Could not load this request's plan/)).toBeInTheDocument();
  });

  it("zooms out and back", async () => {
    (getRequestPlan as Mock).mockResolvedValue(linkedPlan());
    renderPanel();
    await screen.findByTestId("plan-node-Cut");

    const zoomOut = screen.getByRole("button", { name: "Zoom out" });
    await userEvent.click(zoomOut);
    await userEvent.click(zoomOut);
    // 0.5 is the floor; the control disables rather than letting the plan shrink to nothing.
    await waitFor(() => expect(zoomOut).toBeDisabled());

    await userEvent.click(screen.getByRole("button", { name: "Reset zoom" }));
    await waitFor(() => expect(zoomOut).toBeEnabled());

    const zoomIn = screen.getByRole("button", { name: "Zoom in" });
    for (let i = 0; i < 4; i++) await userEvent.click(zoomIn);
    // 2.0 is the ceiling, reached in four steps of 0.25 from 1.
    await waitFor(() => expect(zoomIn).toBeDisabled());
  });

  it("scrolls the new successor into view, so a link off screen still shows itself", async () => {
    const scrollTo = vi.fn();
    // happy-dom has no layout and no Element.scrollTo; the panel guards on it, so stub it to
    // reach the branch that actually moves the viewport.
    Object.defineProperty(Element.prototype, "scrollTo", {
      value: scrollTo, configurable: true, writable: true,
    });
    try {
      (getRequestPlan as Mock)
        .mockResolvedValueOnce(plan())
        .mockResolvedValue(linkedPlan());
      (addRequestDependency as Mock).mockResolvedValue({});
      renderPanel();

      await userEvent.click(await screen.findByTestId("plan-tray-row-Weld"));
      await userEvent.click(screen.getByLabelText("Wait for"));
      await userEvent.click(await screen.findByRole("option", { name: "Cut" }));

      // The successor lands in column 1; the viewport is asked for its centre.
      await waitFor(() =>
        expect(scrollTo).toHaveBeenCalledWith(
          expect.objectContaining({
            left: PLAN_NODE_WIDTH + PLAN_COLUMN_GAP + PLAN_NODE_WIDTH / 2,
            top: PLAN_NODE_HEIGHT / 2,
          }),
        ),
      );
    } finally {
      Reflect.deleteProperty(Element.prototype, "scrollTo");
    }
  });

  it("refuses a drag that would close a loop, without asking the server", async () => {
    (getRequestPlan as Mock).mockResolvedValue(linkedPlan());
    renderPanel();

    // Weld already waits for Cut; dragging Weld's outgoing port back onto Cut closes the loop.
    const port = await outgoingPort("Weld");
    fireEvent.pointerDown(port, { clientX: 0, clientY: 0 });
    fireEvent(document, pointerEvent("pointerup", cutCentre()));

    expect(await screen.findByText(/would make the plan circular/)).toBeInTheDocument();
    expect(addRequestDependency).not.toHaveBeenCalled();
  });
});
