/**
 * Client error reporting — the browser's one way to tell the server that something broke in
 * the browser. Server-side observability is thorough; without this a deploy that only fails in
 * the browser is invisible to Loki and Prometheus.
 *
 * Every report becomes one structured log event on the API (`POST /api/client-errors`), under
 * the request's correlation id, with the release the bundle was built from. Nothing is stored
 * on the client and nothing identifies the person beyond what the session already carries.
 *
 * Sent with `navigator.sendBeacon`: it survives a page unload, needs no CSRF header (the
 * endpoint is exempt and changes no session state) and never blocks the page. Three guards keep
 * a broken page from flooding the server: a stale-chunk error is skipped (the reload handles
 * it), the same message within a minute is sent once, and a page load sends at most ten
 * reports. The server adds a per-IP rate limit behind those.
 *
 * In a development build nothing is sent; the report goes to the console instead.
 */

import { runtimeConfig } from '@foundation/src/config/runtime';
import { buildUrl } from './api-client';
import { API_PATHS } from './api-paths';
import { isStaleChunkError } from './stale-chunk';

export type ClientReportKind = 'error' | 'unhandledrejection' | 'render';
export type WebVitalName = 'LCP' | 'CLS' | 'TTFB' | 'FID' | 'INP';

interface ClientReport {
  kind: ClientReportKind | 'vital';
  message?: string;
  stack?: string;
  componentStack?: string;
  route: string;
  release?: string;
  vitalName?: WebVitalName;
  vitalValue?: number;
}

/** Mirrors `DomainLimits.ClientReport*` on the server, which rejects anything longer. */
const MESSAGE_MAX = 2000;
const STACK_MAX = 8000;

const MAX_REPORTS_PER_PAGE = 10;
const DEDUPE_WINDOW_MS = 60_000;
/** One session in ten reports its vitals; a trend needs no more and the log stays readable. */
export const VITAL_SAMPLE_RATE = 0.1;

const RELEASE = typeof __APP_VERSION__ !== 'undefined' ? __APP_VERSION__ : undefined;

let reportsSent = 0;
const recentMessages = new Map<string, number>();
let handlersInstalled = false;

function truncate(value: string | undefined, max: number): string | undefined {
  if (value === undefined) return undefined;
  return value.length > max ? `${value.slice(0, max - 1)}…` : value;
}

function describe(error: unknown): { message: string; stack?: string } {
  if (error instanceof Error) return { message: error.message || error.name, stack: error.stack };
  if (typeof error === 'string') return { message: error };
  try {
    return { message: JSON.stringify(error) };
  } catch {
    return { message: String(error) };
  }
}

function currentRoute(): string {
  return typeof window === 'undefined' ? '' : window.location.pathname;
}

/** Hands the report to the browser. False when nothing left the page (dev build, or no transport). */
function send(report: ClientReport): boolean {
  if (runtimeConfig.isDev) {
    console.debug('[client-errors] not sent in development:', report);
    return false;
  }
  if (typeof window === 'undefined' || typeof navigator === 'undefined') return false;

  const url = buildUrl(API_PATHS.CLIENT_ERRORS);
  const body = JSON.stringify(report);
  if (typeof navigator.sendBeacon === 'function') {
    return navigator.sendBeacon(url, new Blob([body], { type: 'application/json' }));
  }
  // Older engines: keepalive gives the request the same survive-unload property.
  void fetch(url, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body,
    credentials: 'include',
    keepalive: true,
  }).catch(() => undefined);
  return true;
}

/**
 * Report one browser error. Returns true when a report left the page, false when it was
 * skipped: a stale-chunk error, a repeat within the minute, the per-page cap, or a dev build.
 */
export function reportClientError(
  kind: ClientReportKind,
  error: unknown,
  extra: { componentStack?: string } = {},
): boolean {
  if (isStaleChunkError(error)) return false;

  const { message, stack } = describe(error);
  const key = `${kind}:${message}`;
  const now = Date.now();
  const lastSeen = recentMessages.get(key);
  if (lastSeen !== undefined && now - lastSeen < DEDUPE_WINDOW_MS) return false;
  if (reportsSent >= MAX_REPORTS_PER_PAGE) return false;

  recentMessages.set(key, now);
  reportsSent += 1;
  return send({
    kind,
    message: truncate(message, MESSAGE_MAX),
    stack: truncate(stack, STACK_MAX),
    componentStack: truncate(extra.componentStack, STACK_MAX),
    route: currentRoute(),
    release: RELEASE,
  });
}

/**
 * Report one web vital from a sampled session. `sample` is the draw in [0, 1); the default is
 * random, a test passes a fixed value. Vitals do not count against the error cap.
 */
export function reportWebVital(name: WebVitalName, value: number, sample: number = Math.random()): boolean {
  if (sample >= VITAL_SAMPLE_RATE) return false;
  return send({
    kind: 'vital',
    vitalName: name,
    vitalValue: Math.round(value * 1000) / 1000,
    route: currentRoute(),
    release: RELEASE,
  });
}

/**
 * Wires `window.onerror` and `unhandledrejection` to {@link reportClientError}. Idempotent, so a
 * second caller (or StrictMode) installs nothing twice. The render-time path is the
 * `RouteErrorBoundary`, which reports from `componentDidCatch`.
 */
export function installGlobalErrorHandlers(): void {
  if (handlersInstalled || typeof window === 'undefined') return;
  handlersInstalled = true;
  window.addEventListener('error', (event) => {
    reportClientError('error', event.error ?? event.message);
  });
  window.addEventListener('unhandledrejection', (event) => {
    reportClientError('unhandledrejection', event.reason);
  });
}

/** Test seam: forgets the dedupe window, the per-page count and the installed-handlers flag. */
export function resetClientErrorReporting(): void {
  reportsSent = 0;
  recentMessages.clear();
  handlersInstalled = false;
}
