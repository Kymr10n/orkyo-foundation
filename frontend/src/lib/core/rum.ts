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
 * In development every vital is logged to the console. In a production build LCP, CLS and
 * TTFB go to the server through `client-errors.ts`, sampled, as structured log events; FID and
 * long tasks stay development-only. `initRUM` also installs the global error handlers, so a
 * product's one call at startup covers both.
 */

import { runtimeConfig } from '@foundation/src/config/runtime';
import { installGlobalErrorHandlers, reportWebVital, type WebVitalName } from './client-errors';

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

// ── Reporting ───────────────────────────────────────────────────────────────

function record(vital: WebVital) {
  if (!runtimeConfig.isDev) {
    if (vital.name === "LCP" || vital.name === "CLS" || vital.name === "TTFB")
      reportWebVital(vital.name as WebVitalName, vital.value);
    return;
  }
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
      console.log(
        `[RUM] Navigation: DNS=${(nav.domainLookupEnd - nav.domainLookupStart).toFixed(0)}ms ` +
          `TCP=${(nav.connectEnd - nav.connectStart).toFixed(0)}ms ` +
          `DOMInteractive=${nav.domInteractive.toFixed(0)}ms ` +
          `Load=${nav.loadEventEnd.toFixed(0)}ms`,
      );
    }, 0);
  });
}

function observeLongTasks() {
  if (!("PerformanceObserver" in window)) return;
  if (!PerformanceObserver.supportedEntryTypes?.includes("longtask")) return;
  const obs = new PerformanceObserver((list) => {
    for (const entry of list.getEntries()) {
      if (entry.duration > 100) {
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
 * Initialize RUM. Call once at app startup (e.g. in main.tsx). In every build it installs the
 * global error handlers and observes LCP, CLS and navigation timing; FID and long tasks are
 * development-only noise.
 */
export function initRUM() {
  if (typeof window === "undefined") return;

  installGlobalErrorHandlers();
  observeLCP();
  observeCLS();
  observeNavigation();
  if (!runtimeConfig.isDev) return;

  observeFID();
  observeLongTasks();
}
