/**
 * Real User Monitoring (RUM) — lightweight browser performance collection.
 *
 * Captures:
 *   - Navigation timing (TTFB, DOM interactive, full load)
 *   - Largest Contentful Paint (LCP)
 *   - First Input Delay (FID)
 *   - Cumulative Layout Shift (CLS)
 *   - Long tasks (> 50ms)
 *
 * All metrics are logged to the console in development.
 * To ship metrics to a backend collector, set `VITE_RUM_ENDPOINT`.
 */

import { runtimeConfig } from '@foundation/src/config/runtime';

interface WebVital {
  name: string;
  value: number;
  rating: "good" | "needs-improvement" | "poor";
}

// ── Thresholds (Google Web Vitals) ──────────────────────────────────────────

const THRESHOLDS = {
  LCP: { good: 2500, poor: 4000 },
  FID: { good: 100, poor: 300 },
  CLS: { good: 0.1, poor: 0.25 },
  TTFB: { good: 800, poor: 1800 },
} as const;

function rate(
  name: keyof typeof THRESHOLDS,
  value: number,
): WebVital["rating"] {
  const t = THRESHOLDS[name];
  if (value <= t.good) return "good";
  if (value <= t.poor) return "needs-improvement";
  return "poor";
}

// ── Collected metrics ───────────────────────────────────────────────────────

const metrics: WebVital[] = [];

function record(vital: WebVital) {
  metrics.push(vital);

  if (runtimeConfig.isDev) {
    const colour =
      vital.rating === "good"
        ? "color: green"
        : vital.rating === "poor"
          ? "color: red"
          : "color: orange";
    console.log(
      `%c[RUM] ${vital.name}: ${vital.value.toFixed(1)}ms (${vital.rating})`,
      colour,
    );
  }
}

// ── Observers ───────────────────────────────────────────────────────────────

function observeLCP() {
  if (!("PerformanceObserver" in window)) return;
  if (!PerformanceObserver.supportedEntryTypes?.includes("largest-contentful-paint")) return;
  const obs = new PerformanceObserver((list) => {
    const entries = list.getEntries();
    const last = entries[entries.length - 1];
    if (last) {
      record({
        name: "LCP",
        value: last.startTime,
        rating: rate("LCP", last.startTime),
      });
    }
  });
  obs.observe({ type: "largest-contentful-paint", buffered: true });
}

function observeFID() {
  if (!("PerformanceObserver" in window)) return;
  if (!PerformanceObserver.supportedEntryTypes?.includes("first-input")) return;
  const obs = new PerformanceObserver((list) => {
    for (const entry of list.getEntries()) {
      const fidEntry = entry as PerformanceEventTiming;
      const fid = fidEntry.processingStart - fidEntry.startTime;
      record({ name: "FID", value: fid, rating: rate("FID", fid) });
    }
  });
  obs.observe({ type: "first-input", buffered: true });
}

function observeCLS() {
  if (!("PerformanceObserver" in window)) return;
  if (!PerformanceObserver.supportedEntryTypes?.includes("layout-shift")) return;
  let clsValue = 0;
  const obs = new PerformanceObserver((list) => {
    for (const entry of list.getEntries()) {
      const shift = entry as PerformanceEntry & { hadRecentInput: boolean; value: number };
      if (!shift.hadRecentInput) {
        clsValue += shift.value;
      }
    }
  });
  obs.observe({ type: "layout-shift", buffered: true });

  // Report CLS on page hide (final value)
  document.addEventListener(
    "visibilitychange",
    () => {
      if (document.visibilityState === "hidden") {
        record({ name: "CLS", value: clsValue, rating: rate("CLS", clsValue) });
        obs.disconnect();
      }
    },
    { once: true },
  );
}

function observeNavigation() {
  // Use Navigation Timing API
  window.addEventListener("load", () => {
    // Defer to ensure timing entries are populated
    setTimeout(() => {
      const [nav] = performance.getEntriesByType(
        "navigation",
      ) as PerformanceNavigationTiming[];
      if (!nav) return;

      const ttfb = nav.responseStart - nav.requestStart;
      record({ name: "TTFB", value: ttfb, rating: rate("TTFB", ttfb) });

      if (runtimeConfig.isDev) {
        console.log(
          `[RUM] Navigation: DNS=${(nav.domainLookupEnd - nav.domainLookupStart).toFixed(0)}ms ` +
            `TCP=${(nav.connectEnd - nav.connectStart).toFixed(0)}ms ` +
            `DOMInteractive=${nav.domInteractive.toFixed(0)}ms ` +
            `Load=${nav.loadEventEnd.toFixed(0)}ms`,
        );
      }
    }, 0);
  });
}

function observeLongTasks() {
  if (!("PerformanceObserver" in window)) return;
  if (!PerformanceObserver.supportedEntryTypes?.includes("longtask")) return;
  const obs = new PerformanceObserver((list) => {
    for (const entry of list.getEntries()) {
      if (runtimeConfig.isDev && entry.duration > 100) {
        console.warn(
          `[RUM] Long task: ${entry.duration.toFixed(0)}ms`,
          entry,
        );
      }
    }
  });
  obs.observe({ type: "longtask", buffered: true });
}

// ── Public API ──────────────────────────────────────────────────────────────

/**
 * Initialize all RUM observers. Call once at app startup (e.g. in main.tsx).
 */
export function initRUM() {
  if (typeof window === "undefined") return;

  observeLCP();
  observeFID();
  observeCLS();
  observeNavigation();
  observeLongTasks();
}

/**
 * Get all collected web vitals so far.
 */
export function getMetrics(): readonly WebVital[] {
  return metrics;
}
