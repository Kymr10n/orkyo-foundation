import { afterEach, describe, expect, it, vi } from "vitest";
import { renderHook, act } from "@testing-library/react";
import { useBreakpoint } from "./useBreakpoint";

/**
 * Install a width-driven matchMedia mock that evaluates `(min-width: Npx)`
 * queries against a mutable viewport width and supports change listeners, so a
 * test can resize the viewport and assert the hook reacts.
 */
function mockViewport(initialWidth: number) {
  let width = initialWidth;
  // One registration per (query, callback) pair — the real DOM hands back a
  // distinct MediaQueryList per query, so the hook's two boundary queries each
  // hold their own listener. Track them as a list, not a Set.
  const registrations: (() => void)[] = [];

  const matchMedia = vi.fn((query: string) => {
    const min = /\(min-width:\s*(\d+)px\)/.exec(query);
    return {
      get matches() {
        return min ? width >= Number(min[1]) : false;
      },
      media: query,
      onchange: null,
      addEventListener: (_: string, cb: () => void) => registrations.push(cb),
      removeEventListener: (_: string, cb: () => void) => {
        const i = registrations.indexOf(cb);
        if (i >= 0) registrations.splice(i, 1);
      },
      dispatchEvent: () => false,
    } as unknown as MediaQueryList;
  });

  Object.defineProperty(window, "matchMedia", {
    value: matchMedia,
    writable: true,
    configurable: true,
  });

  return {
    resize(next: number) {
      width = next;
      // De-dupe the (single) React callback so we schedule one update.
      act(() => new Set(registrations).forEach((cb) => cb()));
    },
    listenerCount: () => registrations.length,
  };
}

const originalMatchMedia = window.matchMedia;
afterEach(() => {
  Object.defineProperty(window, "matchMedia", {
    value: originalMatchMedia,
    writable: true,
    configurable: true,
  });
});

describe("useBreakpoint", () => {
  it("reports phone below 768px", () => {
    mockViewport(767);
    const { result } = renderHook(() => useBreakpoint());
    expect(result.current.device).toBe("phone");
    expect(result.current).toMatchObject({ isPhone: true, isTablet: false, isDesktop: false });
  });

  it("reports tablet at 768px (md) and up to 1279px", () => {
    const vp = mockViewport(768);
    const { result } = renderHook(() => useBreakpoint());
    expect(result.current.device).toBe("tablet");

    vp.resize(1279);
    expect(result.current.device).toBe("tablet");
    expect(result.current).toMatchObject({ isPhone: false, isTablet: true, isDesktop: false });
  });

  it("reports desktop at 1280px (xl) and up", () => {
    mockViewport(1280);
    const { result } = renderHook(() => useBreakpoint());
    expect(result.current.device).toBe("desktop");
    expect(result.current).toMatchObject({ isPhone: false, isTablet: false, isDesktop: true });
  });

  it("reacts when the viewport crosses a boundary", () => {
    const vp = mockViewport(1280);
    const { result } = renderHook(() => useBreakpoint());
    expect(result.current.device).toBe("desktop");

    vp.resize(900);
    expect(result.current.device).toBe("tablet");

    vp.resize(500);
    expect(result.current.device).toBe("phone");
  });

  it("subscribes and unsubscribes listeners on mount/unmount", () => {
    const vp = mockViewport(1280);
    const { unmount } = renderHook(() => useBreakpoint());
    // One listener per tracked boundary query (desktop + tablet).
    expect(vp.listenerCount()).toBe(2);

    unmount();
    expect(vp.listenerCount()).toBe(0);
  });

  it("defaults to desktop when matchMedia is unavailable (SSR-safe)", () => {
    Object.defineProperty(window, "matchMedia", {
      value: undefined,
      writable: true,
      configurable: true,
    });
    const { result } = renderHook(() => useBreakpoint());
    expect(result.current.device).toBe("desktop");
  });
});
