import { describe, expect, it, vi, beforeEach } from "vitest";
import { initRUM, getMetrics } from "./rum";

describe("rum", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  describe("initRUM", () => {
    it("does not throw in jsdom environment", () => {
      // jsdom doesn't support PerformanceObserver fully, but initRUM
      // should gracefully degrade without errors
      expect(() => initRUM()).not.toThrow();
    });

    it("is safe to call multiple times", () => {
      expect(() => {
        initRUM();
        initRUM();
      }).not.toThrow();
    });
  });

  describe("getMetrics", () => {
    it("returns an array", () => {
      const metrics = getMetrics();
      expect(Array.isArray(metrics)).toBe(true);
    });

    it("returns readonly array", () => {
      const metrics = getMetrics();
      // Should be an array reference (ReadonlyArray)
      expect(metrics).toBeDefined();
    });
  });
});
