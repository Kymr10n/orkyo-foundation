import { describe, expect, it, vi, beforeEach, afterEach } from "vitest";
import { initRUM, getMetrics } from "./rum";

// Control isDev so we can cover both dev and non-dev record() paths
const { mockRuntimeConfig } = vi.hoisted(() => ({
  mockRuntimeConfig: { isDev: false },
}));
vi.mock("@foundation/src/config/runtime", () => ({
  runtimeConfig: mockRuntimeConfig,
}));

// ── PerformanceObserver test double ──────────────────────────────────────────

type PerfObsCallback = (list: { getEntries: () => object[] }) => void;
let capturedCallbacks: Record<string, PerfObsCallback> = {};

class MockPerformanceObserver {
  private cb: PerfObsCallback;
  constructor(cb: PerfObsCallback) { this.cb = cb; }
  observe({ type }: { type: string }) { capturedCallbacks[type] = this.cb; }
  disconnect() {}
  static supportedEntryTypes = [
    "largest-contentful-paint",
    "first-input",
    "layout-shift",
    "longtask",
  ];
}

describe("rum", () => {
  beforeEach(() => {
    capturedCallbacks = {};
    vi.spyOn(console, "log").mockImplementation(() => {});
    vi.spyOn(console, "warn").mockImplementation(() => {});
    vi.stubGlobal("PerformanceObserver", MockPerformanceObserver);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  describe("initRUM", () => {
    it("does not throw in jsdom environment", () => {
      expect(() => initRUM()).not.toThrow();
    });

    it("is safe to call multiple times", () => {
      expect(() => { initRUM(); initRUM(); }).not.toThrow();
    });

    it("skips all observers when window is undefined", () => {
      const original = globalThis.window;
      // @ts-expect-error intentional
      delete globalThis.window;
      expect(() => initRUM()).not.toThrow();
      globalThis.window = original;
    });
  });

  describe("getMetrics", () => {
    it("returns an array", () => {
      expect(Array.isArray(getMetrics())).toBe(true);
    });
  });

  // ── LCP observer ────────────────────────────────────────────────────────────

  describe("LCP (Largest Contentful Paint)", () => {
    it("records a LCP vital with 'good' rating when LCP ≤ 2500ms", () => {
      initRUM();
      capturedCallbacks["largest-contentful-paint"]?.({
        getEntries: () => [{ startTime: 1000 }],
      });
      const lcp = getMetrics().find((m) => m.name === "LCP");
      expect(lcp).toBeDefined();
      expect(lcp?.rating).toBe("good");
    });

    it("records 'needs-improvement' when LCP is between 2500–4000ms", () => {
      initRUM();
      capturedCallbacks["largest-contentful-paint"]?.({
        getEntries: () => [{ startTime: 3000 }],
      });
      const lcp = getMetrics().find((m) => m.name === "LCP" && m.value === 3000);
      expect(lcp?.rating).toBe("needs-improvement");
    });

    it("records 'poor' when LCP > 4000ms", () => {
      initRUM();
      capturedCallbacks["largest-contentful-paint"]?.({
        getEntries: () => [{ startTime: 5000 }],
      });
      const lcp = getMetrics().find((m) => m.name === "LCP" && m.value === 5000);
      expect(lcp?.rating).toBe("poor");
    });

    it("does nothing when entry list is empty", () => {
      initRUM();
      const before = getMetrics().length;
      capturedCallbacks["largest-contentful-paint"]?.({ getEntries: () => [] });
      expect(getMetrics().length).toBe(before);
    });
  });

  // ── FID observer ────────────────────────────────────────────────────────────

  describe("FID (First Input Delay)", () => {
    it("records a FID vital with 'good' rating when FID ≤ 100ms", () => {
      initRUM();
      capturedCallbacks["first-input"]?.({
        getEntries: () => [{ startTime: 0, processingStart: 50 }],
      });
      const fid = getMetrics().find((m) => m.name === "FID");
      expect(fid?.rating).toBe("good");
    });

    it("records 'poor' when FID > 300ms", () => {
      initRUM();
      capturedCallbacks["first-input"]?.({
        getEntries: () => [{ startTime: 0, processingStart: 400 }],
      });
      const fid = getMetrics().find((m) => m.name === "FID" && m.value === 400);
      expect(fid?.rating).toBe("poor");
    });
  });

  // ── CLS observer ────────────────────────────────────────────────────────────

  describe("CLS (Cumulative Layout Shift)", () => {
    it("accumulates shift values without recent input and reports on visibility change", () => {
      initRUM();
      capturedCallbacks["layout-shift"]?.({
        getEntries: () => [
          { hadRecentInput: false, value: 0.05 },
          { hadRecentInput: false, value: 0.04 },
        ],
      });
      // Trigger visibilitychange → hidden to flush CLS
      Object.defineProperty(document, "visibilityState", {
        value: "hidden",
        configurable: true,
      });
      document.dispatchEvent(new Event("visibilitychange"));
      // Use the LAST CLS metric (earlier initRUM() calls also have listeners with clsValue=0)
      const all = getMetrics().filter((m) => m.name === "CLS");
      const cls = all.at(-1);
      expect(cls).toBeDefined();
      expect(cls?.value).toBeCloseTo(0.09);
      expect(cls?.rating).toBe("good");
    });

    it("ignores shifts that had recent input", () => {
      initRUM();
      const before = getMetrics().filter((m) => m.name === "CLS").length;
      capturedCallbacks["layout-shift"]?.({
        getEntries: () => [{ hadRecentInput: true, value: 0.5 }],
      });
      // Should not flush yet (visibilitychange hasn't fired)
      expect(getMetrics().filter((m) => m.name === "CLS").length).toBe(before);
    });
  });

  // ── record() — dev-mode console output ──────────────────────────────────────

  describe("record() dev-mode logging", () => {
    it("logs with green colour in dev mode for 'good' vitals", () => {
      mockRuntimeConfig.isDev = true;
      initRUM();
      capturedCallbacks["largest-contentful-paint"]?.({
        getEntries: () => [{ startTime: 500 }],
      });
      expect(console.log).toHaveBeenCalledWith(
        expect.stringContaining("[RUM]"),
        "color: green",
      );
      mockRuntimeConfig.isDev = false;
    });

    it("logs with red colour for 'poor' vitals in dev mode", () => {
      mockRuntimeConfig.isDev = true;
      initRUM();
      capturedCallbacks["largest-contentful-paint"]?.({
        getEntries: () => [{ startTime: 5000 }],
      });
      expect(console.log).toHaveBeenCalledWith(
        expect.stringContaining("[RUM]"),
        "color: red",
      );
      mockRuntimeConfig.isDev = false;
    });

    it("does not log in production mode", () => {
      mockRuntimeConfig.isDev = false;
      initRUM();
      capturedCallbacks["largest-contentful-paint"]?.({
        getEntries: () => [{ startTime: 500 }],
      });
      expect(console.log).not.toHaveBeenCalled();
    });
  });

  // ── Long tasks ───────────────────────────────────────────────────────────────

  describe("Long tasks observer", () => {
    it("warns in dev mode for tasks > 100ms", () => {
      mockRuntimeConfig.isDev = true;
      vi.spyOn(console, "warn").mockImplementation(() => {});
      initRUM();
      capturedCallbacks.longtask?.({
        getEntries: () => [{ duration: 150, name: "self", entryType: "longtask" }],
      });
      expect(console.warn).toHaveBeenCalledWith(
        expect.stringContaining("[RUM] Long task:"),
        expect.anything(),
      );
      mockRuntimeConfig.isDev = false;
    });

    it("does not warn in production mode", () => {
      mockRuntimeConfig.isDev = false;
      vi.spyOn(console, "warn").mockImplementation(() => {});
      initRUM();
      capturedCallbacks.longtask?.({
        getEntries: () => [{ duration: 150 }],
      });
      expect(console.warn).not.toHaveBeenCalled();
    });
  });
});
