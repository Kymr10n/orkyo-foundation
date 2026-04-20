import { describe, it, expect, vi, afterEach, type Mock } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useResizeGesture } from "./useResizeGesture";
import type { ResizeGestureCallbacks, ResizeGeometry, ResizeGestureResult } from "./useResizeGesture";
import type { ResizeEdge } from "@/domain/scheduling/schedule-model";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const THRESHOLD_PX = 5;
const MIN_DURATION_MS = 60_000;

/** 8-hour view, 800 px container → 1 px ≈ 36 s */
const GEOMETRY: ResizeGeometry = {
  origStartMs: new Date("2024-01-15T09:00:00Z").getTime(),
  origEndMs: new Date("2024-01-15T11:00:00Z").getTime(),
  containerWidthPx: 800,
  totalDurationMs: 8 * 3_600_000,
};

interface MockCallbacks extends ResizeGestureCallbacks {
  onStart: Mock<(edge: ResizeEdge) => void>;
  onUpdate: Mock<(startMs: number, endMs: number) => void>;
  onCommit: Mock<(result: ResizeGestureResult) => void>;
  onCancel: Mock<() => void>;
}

function makeCallbacks(): MockCallbacks {
  return {
    onStart: vi.fn(),
    onUpdate: vi.fn(),
    onCancel: vi.fn(),
    onCommit: vi.fn(),
  };
}

function makePointerEvent(
  type: string,
  clientX: number,
): React.PointerEvent<HTMLDivElement> {
  return {
    clientX,
    clientY: 10,
    pointerId: 1,
    target: { setPointerCapture: vi.fn() } as unknown as EventTarget,
    stopPropagation: vi.fn(),
    preventDefault: vi.fn(),
    nativeEvent: { stopPropagation: vi.fn() },
  } as unknown as React.PointerEvent<HTMLDivElement>;
}

function pointerMove(clientX: number) {
  document.dispatchEvent(
    new PointerEvent("pointermove", { bubbles: true, clientX, clientY: 10, pointerId: 1 }),
  );
}

function pointerUp() {
  document.dispatchEvent(
    new PointerEvent("pointerup", { bubbles: true, pointerId: 1 }),
  );
}

function pointerCancel() {
  document.dispatchEvent(
    new PointerEvent("pointercancel", { bubbles: true, pointerId: 1 }),
  );
}

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe("useResizeGesture", () => {
  afterEach(() => {
    // Clean up any leftover document listeners (unmount handles this too)
  });

  it("does not activate when movement is below threshold", () => {
    const cb = makeCallbacks();
    const { result } = renderHook(() =>
      useResizeGesture(cb, { thresholdPx: THRESHOLD_PX, minDurationMs: MIN_DURATION_MS }),
    );

    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 100), "right", GEOMETRY);
    });

    act(() => pointerMove(100 + THRESHOLD_PX - 2));
    expect(cb.onStart).not.toHaveBeenCalled();
    expect(cb.onUpdate).not.toHaveBeenCalled();

    act(() => pointerUp());
    expect(cb.onCancel).toHaveBeenCalledTimes(1);
    expect(cb.onCommit).not.toHaveBeenCalled();
  });

  it("activates after threshold is exceeded", () => {
    const cb = makeCallbacks();
    const { result } = renderHook(() =>
      useResizeGesture(cb, { thresholdPx: THRESHOLD_PX, minDurationMs: MIN_DURATION_MS }),
    );

    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 100), "right", GEOMETRY);
    });

    act(() => pointerMove(100 + THRESHOLD_PX + 20));
    expect(cb.onStart).toHaveBeenCalledTimes(1);
    expect(cb.onStart).toHaveBeenCalledWith("right");
    expect(cb.onUpdate).toHaveBeenCalledTimes(1);
  });

  it("calls onCommit with final bounds on pointer up after drag", () => {
    const cb = makeCallbacks();
    const { result } = renderHook(() =>
      useResizeGesture(cb, { thresholdPx: THRESHOLD_PX, minDurationMs: MIN_DURATION_MS }),
    );

    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 100), "right", GEOMETRY);
    });

    act(() => pointerMove(100 + THRESHOLD_PX + 50));
    act(() => pointerUp());

    expect(cb.onCommit).toHaveBeenCalledTimes(1);
    const { startMs, endMs } = cb.onCommit.mock.calls[0][0];
    // Start should be unchanged (right-edge drag)
    expect(startMs).toBe(GEOMETRY.origStartMs);
    // End should be later than original
    expect(endMs).toBeGreaterThan(GEOMETRY.origEndMs);
  });

  it("calls onCancel on pointer up without movement", () => {
    const cb = makeCallbacks();
    const { result } = renderHook(() =>
      useResizeGesture(cb, { thresholdPx: THRESHOLD_PX, minDurationMs: MIN_DURATION_MS }),
    );

    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 100), "right", GEOMETRY);
    });

    act(() => pointerUp());

    expect(cb.onCancel).toHaveBeenCalledTimes(1);
    expect(cb.onCommit).not.toHaveBeenCalled();
  });

  it("reports left-edge resize with earlier start time", () => {
    const cb = makeCallbacks();
    const { result } = renderHook(() =>
      useResizeGesture(cb, { thresholdPx: THRESHOLD_PX, minDurationMs: MIN_DURATION_MS }),
    );

    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 200), "left", GEOMETRY);
    });

    // Drag left (negative delta)
    act(() => pointerMove(200 - THRESHOLD_PX - 40));
    act(() => pointerUp());

    expect(cb.onStart).toHaveBeenCalledWith("left");
    expect(cb.onCommit).toHaveBeenCalledTimes(1);
    const { startMs, endMs } = cb.onCommit.mock.calls[0][0];
    expect(startMs).toBeLessThan(GEOMETRY.origStartMs);
    expect(endMs).toBe(GEOMETRY.origEndMs);
  });

  it("enforces minimum duration on right edge", () => {
    const cb = makeCallbacks();
    const { result } = renderHook(() =>
      useResizeGesture(cb, { thresholdPx: THRESHOLD_PX, minDurationMs: MIN_DURATION_MS }),
    );

    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 100), "right", GEOMETRY);
    });

    // Drag far left to shrink below minimum
    act(() => pointerMove(100 - 500));
    act(() => pointerUp());

    expect(cb.onCommit).toHaveBeenCalledTimes(1);
    const { startMs, endMs } = cb.onCommit.mock.calls[0][0];
    expect(endMs - startMs).toBeGreaterThanOrEqual(MIN_DURATION_MS);
  });

  it("enforces minimum duration on left edge", () => {
    const cb = makeCallbacks();
    const { result } = renderHook(() =>
      useResizeGesture(cb, { thresholdPx: THRESHOLD_PX, minDurationMs: MIN_DURATION_MS }),
    );

    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 200), "left", GEOMETRY);
    });

    // Drag far right to shrink below minimum
    act(() => pointerMove(200 + 500));
    act(() => pointerUp());

    expect(cb.onCommit).toHaveBeenCalledTimes(1);
    const { startMs, endMs } = cb.onCommit.mock.calls[0][0];
    expect(endMs - startMs).toBeGreaterThanOrEqual(MIN_DURATION_MS);
  });

  it("tracks progressive updates during drag", () => {
    const cb = makeCallbacks();
    const { result } = renderHook(() =>
      useResizeGesture(cb, { thresholdPx: THRESHOLD_PX, minDurationMs: MIN_DURATION_MS }),
    );

    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 100), "right", GEOMETRY);
    });

    act(() => pointerMove(100 + THRESHOLD_PX + 20));
    act(() => pointerMove(100 + THRESHOLD_PX + 60));

    // onStart called once, onUpdate called twice
    expect(cb.onStart).toHaveBeenCalledTimes(1);
    expect(cb.onUpdate).toHaveBeenCalledTimes(2);

    // Second update should have a larger endMs than first
    const [s1, e1] = cb.onUpdate.mock.calls[0];
    const [s2, e2] = cb.onUpdate.mock.calls[1];
    expect(s1).toBe(s2); // start unchanged for right-edge
    expect(e2).toBeGreaterThan(e1);

    act(() => pointerUp());
  });

  it("cleans up document listeners after pointer up", () => {
    const cb = makeCallbacks();
    const { result } = renderHook(() =>
      useResizeGesture(cb, { thresholdPx: THRESHOLD_PX, minDurationMs: MIN_DURATION_MS }),
    );

    const addSpy = vi.spyOn(document, "addEventListener");
    const removeSpy = vi.spyOn(document, "removeEventListener");

    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 100), "right", GEOMETRY);
    });

    expect(addSpy.mock.calls.some(([ev]) => ev === "pointermove")).toBe(true);
    expect(addSpy.mock.calls.some(([ev]) => ev === "pointerup")).toBe(true);
    expect(addSpy.mock.calls.some(([ev]) => ev === "pointercancel")).toBe(true);

    act(() => pointerMove(100 + THRESHOLD_PX + 30));
    act(() => pointerUp());

    expect(removeSpy.mock.calls.some(([ev]) => ev === "pointermove")).toBe(true);
    expect(removeSpy.mock.calls.some(([ev]) => ev === "pointerup")).toBe(true);
    expect(removeSpy.mock.calls.some(([ev]) => ev === "pointercancel")).toBe(true);

    addSpy.mockRestore();
    removeSpy.mockRestore();
  });

  it("sets lastCommitMsRef on commit", () => {
    const cb = makeCallbacks();
    const { result } = renderHook(() =>
      useResizeGesture(cb, { thresholdPx: THRESHOLD_PX, minDurationMs: MIN_DURATION_MS }),
    );

    expect(result.current.lastCommitMsRef.current).toBe(0);

    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 100), "right", GEOMETRY);
    });
    act(() => pointerMove(100 + THRESHOLD_PX + 30));
    act(() => pointerUp());

    expect(result.current.lastCommitMsRef.current).toBeGreaterThan(0);
  });

  it("does not set lastCommitMsRef on cancel", () => {
    const cb = makeCallbacks();
    const { result } = renderHook(() =>
      useResizeGesture(cb, { thresholdPx: THRESHOLD_PX, minDurationMs: MIN_DURATION_MS }),
    );

    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 100), "right", GEOMETRY);
    });
    act(() => pointerUp());

    expect(result.current.lastCommitMsRef.current).toBe(0);
  });

  it("uses latest callback refs (no stale closures)", () => {
    const cb1 = makeCallbacks();
    const cb2 = makeCallbacks();

    const { result, rerender } = renderHook(
      ({ callbacks }) =>
        useResizeGesture(callbacks, { thresholdPx: THRESHOLD_PX, minDurationMs: MIN_DURATION_MS }),
      { initialProps: { callbacks: cb1 } },
    );

    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 100), "right", GEOMETRY);
    });

    // Swap callbacks mid-gesture
    rerender({ callbacks: cb2 });

    act(() => pointerMove(100 + THRESHOLD_PX + 30));

    // New callbacks should be called, not old ones
    expect(cb1.onStart).not.toHaveBeenCalled();
    expect(cb2.onStart).toHaveBeenCalledTimes(1);

    act(() => pointerUp());
    expect(cb2.onCommit).toHaveBeenCalledTimes(1);
    expect(cb1.onCommit).not.toHaveBeenCalled();
  });

  it("calls onCancel when pointercancel fires during gesture", () => {
    const cb = makeCallbacks();
    const { result } = renderHook(() =>
      useResizeGesture(cb, { thresholdPx: THRESHOLD_PX, minDurationMs: MIN_DURATION_MS }),
    );

    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 100), "right", GEOMETRY);
    });

    // Move past threshold to activate, then cancel
    act(() => pointerMove(100 + THRESHOLD_PX + 20));
    expect(cb.onStart).toHaveBeenCalledTimes(1);

    act(() => pointerCancel());

    // pointercancel after activation → commit with latest bounds (same as pointerup)
    expect(cb.onCommit).toHaveBeenCalledTimes(1);
  });

  it("calls onCancel when pointercancel fires before activation", () => {
    const cb = makeCallbacks();
    const { result } = renderHook(() =>
      useResizeGesture(cb, { thresholdPx: THRESHOLD_PX, minDurationMs: MIN_DURATION_MS }),
    );

    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 100), "right", GEOMETRY);
    });

    act(() => pointerCancel());

    expect(cb.onCancel).toHaveBeenCalledTimes(1);
    expect(cb.onCommit).not.toHaveBeenCalled();
  });

  it("registers document listeners synchronously (no render gap)", () => {
    const cb = makeCallbacks();
    const addSpy = vi.spyOn(document, "addEventListener");

    const { result } = renderHook(() =>
      useResizeGesture(cb, { thresholdPx: THRESHOLD_PX, minDurationMs: MIN_DURATION_MS }),
    );

    // Clear any listeners from the useEffect (cleanup registration)
    addSpy.mockClear();

    // beginGesture should register listeners synchronously
    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 100), "right", GEOMETRY);

      // WITHIN THE SAME act(), listeners must already be registered
      const moveListeners = addSpy.mock.calls.filter(([ev]) => ev === "pointermove");
      const upListeners = addSpy.mock.calls.filter(([ev]) => ev === "pointerup");
      expect(moveListeners.length).toBe(1);
      expect(upListeners.length).toBe(1);
    });

    // Clean up
    act(() => pointerUp());
    addSpy.mockRestore();
  });

  it("handles rapid pointer-down and pointer-up (no stuck gesture)", () => {
    const cb = makeCallbacks();
    const { result } = renderHook(() =>
      useResizeGesture(cb, { thresholdPx: THRESHOLD_PX, minDurationMs: MIN_DURATION_MS }),
    );

    // Pointer down + immediate up (user taps without moving)
    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 100), "right", GEOMETRY);
      pointerUp();
    });

    expect(cb.onCancel).toHaveBeenCalledTimes(1);

    // Should be able to start a new gesture — no stuck state
    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 200), "left", GEOMETRY);
    });

    act(() => pointerMove(200 - THRESHOLD_PX - 30));
    expect(cb.onStart).toHaveBeenCalledWith("left");

    act(() => pointerUp());
    expect(cb.onCommit).toHaveBeenCalledTimes(1);
  });

  it("does not leave dangling listeners after unmount mid-gesture", () => {
    const cb = makeCallbacks();
    const removeSpy = vi.spyOn(document, "removeEventListener");

    const { result, unmount } = renderHook(() =>
      useResizeGesture(cb, { thresholdPx: THRESHOLD_PX, minDurationMs: MIN_DURATION_MS }),
    );

    act(() => {
      result.current.beginGesture(makePointerEvent("pointerdown", 100), "right", GEOMETRY);
    });

    act(() => pointerMove(100 + THRESHOLD_PX + 20));
    // gesture is active — now unmount
    unmount();

    expect(removeSpy.mock.calls.some(([ev]) => ev === "pointermove")).toBe(true);
    expect(removeSpy.mock.calls.some(([ev]) => ev === "pointerup")).toBe(true);
    expect(removeSpy.mock.calls.some(([ev]) => ev === "pointercancel")).toBe(true);

    removeSpy.mockRestore();
  });
});
