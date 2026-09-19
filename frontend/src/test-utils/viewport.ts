import { vi } from 'vitest';

// Bound on capture: a bare `window.matchMedia` reference is detached from its receiver.
const originalMatchMedia = window.matchMedia.bind(window);

/**
 * Stub `window.matchMedia` so `(min-width: Npx)` queries answer for a viewport of the
 * given width. happy-dom does no layout, so this is how a test picks the phone or desktop
 * branch of a responsive component. Pair with `afterEach(restoreViewport)`.
 */
export function setViewport(width: number) {
  Object.defineProperty(window, 'matchMedia', {
    writable: true,
    configurable: true,
    value: vi.fn((query: string) => {
      const min = /\(min-width:\s*(\d+)px\)/.exec(query);
      return {
        matches: min ? width >= Number(min[1]) : false,
        media: query,
        onchange: null,
        addEventListener: () => {},
        removeEventListener: () => {},
        dispatchEvent: () => false,
      } as unknown as MediaQueryList;
    }),
  });
}

/** Put the real `window.matchMedia` back. */
export function restoreViewport() {
  Object.defineProperty(window, 'matchMedia', {
    value: originalMatchMedia,
    writable: true,
    configurable: true,
  });
}
