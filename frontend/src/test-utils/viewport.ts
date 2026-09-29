import { vi } from 'vitest';

/** The width every test runs at unless it picks another: a desktop. */
export const DEFAULT_TEST_VIEWPORT_WIDTH = 1280;

/**
 * A width-driven `window.matchMedia`. happy-dom does no layout, so this is how a test picks the
 * phone, tablet or desktop branch of a responsive component. It answers `(min-width: Npx)` and
 * `(max-width: Npx)` (both, when a query has both) against the current width; any other query
 * does not match. `setWidth` changes the width and fires the registered change listeners.
 */
export function createMatchMedia(initialWidth: number) {
  let width = initialWidth;
  // One registration per (query, callback) pair: the real DOM hands back a distinct
  // MediaQueryList per query, so each boundary query holds its own listener.
  const registrations: (() => void)[] = [];

  const evaluate = (query: string) => {
    const min = /\(min-width:\s*(\d+)px\)/.exec(query);
    const max = /\(max-width:\s*(\d+)px\)/.exec(query);
    if (!min && !max) return false;
    return (!min || width >= Number(min[1])) && (!max || width <= Number(max[1]));
  };
  const add = (cb: () => void) => registrations.push(cb);
  const remove = (cb: () => void) => {
    const i = registrations.indexOf(cb);
    if (i >= 0) registrations.splice(i, 1);
  };

  const matchMedia = vi.fn(
    (query: string) =>
      ({
        get matches() {
          return evaluate(query);
        },
        media: query,
        onchange: null,
        addEventListener: (_: string, cb: () => void) => add(cb),
        removeEventListener: (_: string, cb: () => void) => remove(cb),
        addListener: add,
        removeListener: remove,
        dispatchEvent: () => false,
      }) as unknown as MediaQueryList,
  );

  return {
    matchMedia,
    /** Change the width and notify every listener once. Wrap in `act` when React listens. */
    setWidth(next: number) {
      width = next;
      new Set(registrations).forEach((cb) => cb());
    },
    listenerCount: () => registrations.length,
  };
}

/**
 * Install a {@link createMatchMedia} fake at `width` on `window`. Pair with
 * `afterEach(restoreViewport)`.
 */
export function setViewport(width: number) {
  const viewport = createMatchMedia(width);
  Object.defineProperty(window, 'matchMedia', {
    value: viewport.matchMedia,
    writable: true,
    configurable: true,
  });
  return viewport;
}

/** Put the default desktop viewport back. */
export function restoreViewport() {
  setViewport(DEFAULT_TEST_VIEWPORT_WIDTH);
}
