import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useDebouncedCallback } from "./useDebouncedCallback";

describe("useDebouncedCallback", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it("fires once after the quiet period", () => {
    const spy = vi.fn();
    const { result } = renderHook(() => useDebouncedCallback(spy, 200));
    act(() => {
      result.current("a");
      result.current("b");
      result.current("c");
    });
    expect(spy).not.toHaveBeenCalled();
    act(() => vi.advanceTimersByTime(200));
    expect(spy).toHaveBeenCalledTimes(1);
    expect(spy).toHaveBeenCalledWith("c");
  });

  it("keeps a stable identity across renders", () => {
    const { result, rerender } = renderHook(({ cb }) => useDebouncedCallback(cb, 200), {
      initialProps: { cb: vi.fn() },
    });
    const first = result.current;
    rerender({ cb: vi.fn() });
    expect(result.current).toBe(first);
  });

  it("calls the latest callback even though identity is stable", () => {
    const a = vi.fn();
    const b = vi.fn();
    const { result, rerender } = renderHook(({ cb }) => useDebouncedCallback(cb, 200), {
      initialProps: { cb: a },
    });
    rerender({ cb: b });
    act(() => result.current());
    act(() => vi.advanceTimersByTime(200));
    expect(a).not.toHaveBeenCalled();
    expect(b).toHaveBeenCalledTimes(1);
  });

  it("cancel() prevents a pending invocation", () => {
    const spy = vi.fn();
    const { result } = renderHook(() => useDebouncedCallback(spy, 200));
    act(() => {
      result.current();
      result.current.cancel();
      vi.advanceTimersByTime(200);
    });
    expect(spy).not.toHaveBeenCalled();
  });

  it("clears a pending timer on unmount", () => {
    const spy = vi.fn();
    const { result, unmount } = renderHook(() => useDebouncedCallback(spy, 200));
    act(() => result.current());
    unmount();
    act(() => vi.advanceTimersByTime(200));
    expect(spy).not.toHaveBeenCalled();
  });
});
