/**
 * Stale-chunk recovery — heals the "reopened a stale tab after a deploy" failure.
 *
 * Every deploy replaces the content-hashed chunk filenames baked into the
 * frontend image. A tab still holding the previous index.html requests chunk
 * URLs that no longer exist, and the failed dynamic import surfaces as a
 * module-load / MIME-type error (e.g. Safari's "'text/html' is not a valid
 * JavaScript MIME type"). The remedy is always the same: reload, so the browser
 * fetches the current index.html (served no-cache) with valid chunk references.
 *
 * Two cooperating layers:
 *   - `initStaleChunkReload()` — listens for Vite's `vite:preloadError` and
 *     reloads once per guard window. Products call it from their main.tsx
 *     (explicit wiring, like `initTheme()` / `initRUM()`).
 *   - `isStaleChunkError()` — lets `RouteErrorBoundary` recognize chunk-load
 *     failures that bypass the preload helper and offer a reload instead of a
 *     state reset that can never succeed.
 */

import { logger } from '@foundation/src/lib/core/logger';

const RELOAD_GUARD_KEY = 'orkyo:stale-chunk-reloaded-at';
const RELOAD_GUARD_WINDOW_MS = 30_000;

// Module-load failure messages: WebKit, Chromium, Firefox, older Safari.
const STALE_CHUNK_PATTERNS = [
  /is not a valid JavaScript MIME type/i,
  /Failed to fetch dynamically imported module/i,
  /error loading dynamically imported module/i,
  /Importing a module script failed/i,
];

/** True when the error looks like a failed dynamic chunk import. */
export function isStaleChunkError(error: unknown): boolean {
  const message =
    error instanceof Error ? error.message : typeof error === 'string' ? error : '';
  return STALE_CHUNK_PATTERNS.some((pattern) => pattern.test(message));
}

/**
 * Reload the page to pick up the current deployment, at most once per guard
 * window per tab. Returns true when the reload was issued. Skips the reload
 * (returns false) when sessionStorage is unavailable — without the guard an
 * auto-reload could loop forever on a genuinely broken deploy.
 */
export function reloadForNewVersion(): boolean {
  try {
    const lastReloadAt = Number(sessionStorage.getItem(RELOAD_GUARD_KEY) ?? 0);
    if (Date.now() - lastReloadAt < RELOAD_GUARD_WINDOW_MS) return false;
    sessionStorage.setItem(RELOAD_GUARD_KEY, String(Date.now()));
  } catch {
    return false;
  }
  window.location.reload();
  return true;
}

/**
 * Auto-recover from failed chunk preloads. Call once at app startup.
 * When the guard suppresses the reload, the error propagates to the nearest
 * RouteErrorBoundary, which shows a manual reload prompt via isStaleChunkError.
 */
export function initStaleChunkReload(): void {
  if (typeof window === 'undefined') return;
  window.addEventListener('vite:preloadError', (event) => {
    logger.warn('Chunk preload failed — likely a stale tab after a deploy', event.payload);
    if (reloadForNewVersion()) {
      event.preventDefault();
    }
  });
}
