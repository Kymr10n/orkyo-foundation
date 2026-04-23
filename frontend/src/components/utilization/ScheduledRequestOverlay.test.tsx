import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, act } from "@testing-library/react";
import { DndContext } from "@dnd-kit/core";
import { ScheduledRequestOverlay } from "./ScheduledRequestOverlay";
import { useSchedulerStore, RESIZE_MOVE_THRESHOLD_PX, MIN_DURATION_FLOOR_MS } from "@foundation/src/store/scheduler-store";
import { buildIndex } from "@foundation/src/domain/scheduling/schedule-index";
import type { PreviewEntry, PreviewSchedule, ValidationResult } from "@foundation/src/domain/scheduling/schedule-model";
import type { Request } from "@foundation/src/types/requests";
import type { TimeColumn } from "./scheduler-types";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const T = (iso: string) => new Date(iso).getTime();

// 8-hour view: 08:00 – 16:00
const VIEW_START = new Date("2024-01-15T08:00:00Z");
const VIEW_END = new Date("2024-01-15T16:00:00Z");

const COLUMNS: TimeColumn[] = [
  { start: VIEW_START, end: VIEW_END, label: "08:00–16:00" },
];

/** Container width mock for getBoundingClientRect (happy-dom has no layout). */
const CONTAINER_WIDTH_PX = 800;

function makeRequest(overrides: Partial<Request> = {}): Request {
  return {
    id: "req-1",
    name: "Test Request",
    planningMode: "leaf",
    sortOrder: 0,
    spaceId: "space-1",
    startTs: "2024-01-15T09:00:00Z",
    endTs: "2024-01-15T11:00:00Z",
    minimalDurationValue: 120,
    minimalDurationUnit: "minutes",
    schedulingSettingsApply: false,
    status: "planned",
    createdAt: "2024-01-01T00:00:00Z",
    updatedAt: "2024-01-01T00:00:00Z",
    ...overrides,
  };
}

function makeEntry(request: Request, isDraft = false): PreviewEntry {
  return {
    requestId: request.id,
    spaceId: request.spaceId!,
    startMs: T(request.startTs!),
    endMs: T(request.endTs!),
    name: request.name,
    minimalDurationMs: 7_200_000,
    isDraft,
  };
}

function makeScheduleAndIndex(entries: PreviewEntry[]) {
  const schedule: PreviewSchedule = new Map();
  for (const e of entries) schedule.set(e.requestId, e);
  return buildIndex(schedule);
}

const EMPTY_VALIDATION: ValidationResult = new Map();

interface RenderOptions {
  request?: Request;
  entry?: PreviewEntry;
  onRequestResize?: (id: string, startTs: string, endTs: string) => void;
}

function renderOverlay(opts: RenderOptions = {}) {
  const request = opts.request ?? makeRequest();
  const entry = opts.entry ?? makeEntry(request);
  const index = makeScheduleAndIndex([entry]);
  const onRequestClick = vi.fn();
  const onRequestResize = opts.onRequestResize ?? vi.fn();

  const utils = render(
    <DndContext>
      <div style={{ width: `${CONTAINER_WIDTH_PX}px`, position: "relative" }}>
        <ScheduledRequestOverlay
          request={request}
          entry={entry}
          columns={COLUMNS}
          scheduleIndex={index}
          validation={EMPTY_VALIDATION}
          onRequestClick={onRequestClick}
          onRequestResize={onRequestResize}
        />
      </div>
    </DndContext>,
  );

  // happy-dom has no layout — mock getBoundingClientRect on the parent
  const overlay = screen.getByTitle(/Test Request/);
  const parent = overlay.parentElement!;
  parent.getBoundingClientRect = () => ({
    x: 0, y: 0,
    width: CONTAINER_WIDTH_PX, height: 44,
    top: 0, left: 0, right: CONTAINER_WIDTH_PX, bottom: 44,
    toJSON: () => {},
  });

  return { ...utils, onRequestResize, onRequestClick };
}

function pointerDown(target: Element, clientX: number) {
  target.dispatchEvent(
    new PointerEvent("pointerdown", {
      bubbles: true, cancelable: true, pointerId: 1, clientX, clientY: 10,
    }),
  );
}

function pointerMove(clientX: number) {
  document.dispatchEvent(
    new PointerEvent("pointermove", {
      bubbles: true, cancelable: true, pointerId: 1, clientX, clientY: 10,
    }),
  );
}

function pointerUp() {
  document.dispatchEvent(
    new PointerEvent("pointerup", { bubbles: true, cancelable: true, pointerId: 1 }),
  );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("ScheduledRequestOverlay resize", () => {
  beforeEach(() => useSchedulerStore.setState({ draft: null }));
  afterEach(() => useSchedulerStore.setState({ draft: null }));

  function getResizeHandles() {
    const overlay = screen.getByTitle(/Test Request/);
    // Note: happy-dom does not support the `:scope` pseudo-class in selectors,
    // so iterate the live children list directly.
    const children = Array.from(overlay.children);
    const left = children.find((el) => el.classList.contains("left-0"));
    const right = children.find((el) => el.classList.contains("right-0"));
    return { overlay, left: left!, right: right! };
  }

  it("renders left and right resize handles", () => {
    renderOverlay();
    const { left, right } = getResizeHandles();
    expect(left).toBeTruthy();
    expect(right).toBeTruthy();
    expect(left.classList.contains("cursor-ew-resize")).toBe(true);
    expect(right.classList.contains("cursor-ew-resize")).toBe(true);
  });

  it("calls onRequestResize after right-edge drag beyond threshold", () => {
    const onRequestResize = vi.fn();
    renderOverlay({ onRequestResize });
    const { right } = getResizeHandles();

    act(() => pointerDown(right, 100));
    act(() => pointerMove(100 + RESIZE_MOVE_THRESHOLD_PX + 50));

    expect(useSchedulerStore.getState().draft).not.toBeNull();
    expect(useSchedulerStore.getState().draft?.kind).toBe("resize");
    const draft = useSchedulerStore.getState().draft;
    expect(draft?.kind === "resize" && draft.edge).toBe("right");

    act(() => pointerUp());

    // Draft stays alive in "committing" phase until mutation settles
    expect(useSchedulerStore.getState().draft).not.toBeNull();
    expect(useSchedulerStore.getState().draft!.phase).toBe("committing");
    expect(onRequestResize).toHaveBeenCalledTimes(1);
    expect(onRequestResize).toHaveBeenCalledWith("req-1", expect.any(String), expect.any(String));

    const [, , endTs] = onRequestResize.mock.calls[0];
    expect(new Date(endTs).getTime()).toBeGreaterThan(T("2024-01-15T11:00:00Z"));
  });

  it("calls onRequestResize after left-edge drag beyond threshold", () => {
    const onRequestResize = vi.fn();
    renderOverlay({ onRequestResize });
    const { left } = getResizeHandles();

    act(() => pointerDown(left, 200));
    act(() => pointerMove(200 - RESIZE_MOVE_THRESHOLD_PX - 50));

    const draft = useSchedulerStore.getState().draft;
    expect(draft?.kind === "resize" && draft.edge).toBe("left");

    act(() => pointerUp());

    // Draft stays alive in "committing" phase until mutation settles
    expect(useSchedulerStore.getState().draft).not.toBeNull();
    expect(useSchedulerStore.getState().draft!.phase).toBe("committing");
    expect(onRequestResize).toHaveBeenCalledTimes(1);

    const [, startTs] = onRequestResize.mock.calls[0];
    expect(new Date(startTs).getTime()).toBeLessThan(T("2024-01-15T09:00:00Z"));
  });

  it("does NOT trigger resize when movement is below threshold", () => {
    const onRequestResize = vi.fn();
    renderOverlay({ onRequestResize });
    const { right } = getResizeHandles();

    act(() => pointerDown(right, 100));
    act(() => pointerMove(100 + RESIZE_MOVE_THRESHOLD_PX - 2));

    expect(useSchedulerStore.getState().draft).toBeNull();

    act(() => pointerUp());
    expect(onRequestResize).not.toHaveBeenCalled();
  });

  it("does NOT trigger resize on immediate pointer up (click)", () => {
    const onRequestResize = vi.fn();
    renderOverlay({ onRequestResize });
    const { right } = getResizeHandles();

    act(() => pointerDown(right, 100));
    act(() => pointerUp());

    expect(useSchedulerStore.getState().draft).toBeNull();
    expect(onRequestResize).not.toHaveBeenCalled();
  });

  it("enforces minimum duration floor on right edge", () => {
    const onRequestResize = vi.fn();
    renderOverlay({ onRequestResize });
    const { right } = getResizeHandles();

    act(() => pointerDown(right, 100));
    act(() => pointerMove(100 - 500));
    act(() => pointerUp());

    expect(onRequestResize).toHaveBeenCalledTimes(1);
    const [, startTs, endTs] = onRequestResize.mock.calls[0];
    expect(new Date(endTs).getTime() - new Date(startTs).getTime()).toBeGreaterThanOrEqual(MIN_DURATION_FLOOR_MS);
  });

  it("enforces minimum duration floor on left edge", () => {
    const onRequestResize = vi.fn();
    renderOverlay({ onRequestResize });
    const { left } = getResizeHandles();

    act(() => pointerDown(left, 200));
    act(() => pointerMove(200 + 500));
    act(() => pointerUp());

    expect(onRequestResize).toHaveBeenCalledTimes(1);
    const [, startTs, endTs] = onRequestResize.mock.calls[0];
    expect(new Date(endTs).getTime() - new Date(startTs).getTime()).toBeGreaterThanOrEqual(MIN_DURATION_FLOOR_MS);
  });

  it("updates store draft progressively during drag", () => {
    renderOverlay();
    const { right } = getResizeHandles();

    act(() => pointerDown(right, 100));
    act(() => pointerMove(100 + RESIZE_MOVE_THRESHOLD_PX + 20));

    const draft1 = useSchedulerStore.getState().draft;
    expect(draft1?.kind).toBe("resize");
    const end1 = draft1?.kind === "resize" ? draft1.previewEndMs : 0;

    act(() => pointerMove(100 + RESIZE_MOVE_THRESHOLD_PX + 60));

    const draft2 = useSchedulerStore.getState().draft;
    const end2 = draft2?.kind === "resize" ? draft2.previewEndMs : 0;
    expect(end2).toBeGreaterThan(end1);

    act(() => pointerUp());
  });

  it("cleans up document listeners after pointer up", () => {
    renderOverlay();
    const { right } = getResizeHandles();

    const addSpy = vi.spyOn(document, "addEventListener");
    const removeSpy = vi.spyOn(document, "removeEventListener");

    act(() => pointerDown(right, 100));

    expect(addSpy.mock.calls.some(([ev]) => ev === "pointermove")).toBe(true);
    expect(addSpy.mock.calls.some(([ev]) => ev === "pointerup")).toBe(true);

    act(() => pointerMove(100 + RESIZE_MOVE_THRESHOLD_PX + 30));
    act(() => pointerUp());

    expect(removeSpy.mock.calls.some(([ev]) => ev === "pointermove")).toBe(true);
    expect(removeSpy.mock.calls.some(([ev]) => ev === "pointerup")).toBe(true);

    addSpy.mockRestore();
    removeSpy.mockRestore();
  });
});
