import '@testing-library/jest-dom';
import { afterEach, vi } from 'vitest';
import { cleanup } from '@testing-library/react';
import type * as PermissionsModule from '@foundation/src/hooks/usePermissions';

// Default the permission gate to "can edit" so component tests render their write
// affordances and submit buttons enabled, as before this hook existed. Tests that exercise
// the Viewer / read-only state override with vi.mocked(useCanEdit).mockReturnValue(false).
// The hook's own test (usePermissions.test.ts) unmocks this to test the real implementation.
vi.mock('@foundation/src/hooks/usePermissions', async (importOriginal) => {
  const actual = await importOriginal<typeof PermissionsModule>();
  return { ...actual, useCanEdit: vi.fn(() => true), useIsTenantAdmin: vi.fn(() => true) };
});

// happy-dom ships ResizeObserver but it never fires, so @tanstack/react-virtual
// never learns the scroll container height and renders an empty virtual list.
// Replace it with a stub that fires synchronously with a non-zero bounding rect.
globalThis.ResizeObserver = class implements ResizeObserver {
  private cb: ResizeObserverCallback;
  constructor(cb: ResizeObserverCallback) { this.cb = cb; }
  observe(target: Element): void {
    this.cb(
      [{
        target,
        contentRect: new DOMRect(0, 0, 1200, 800) as unknown as DOMRectReadOnly,
        borderBoxSize: [{ inlineSize: 1200, blockSize: 800 }],
        contentBoxSize: [{ inlineSize: 1200, blockSize: 800 }],
        devicePixelContentBoxSize: [],
      }],
      this,
    );
  }
  unobserve(): void {}
  disconnect(): void {}
};

// happy-dom has no layout engine, so window.matchMedia is unreliable. Provide a
// width-based polyfill defaulting to a desktop viewport (1280px) so existing tests
// keep their desktop behavior and responsive units (useBreakpoint,
// DetailDrawer, mobile nav) are testable. Tests drive a specific breakpoint by
// overriding window.matchMedia (see useBreakpoint.test.tsx). writable/configurable so
// those overrides — and the theme tests in app-store.test.ts — still work.
const DEFAULT_TEST_VIEWPORT_WIDTH = 1280;
Object.defineProperty(window, 'matchMedia', {
  writable: true,
  configurable: true,
  value: (query: string): MediaQueryList => {
    const min = /\(min-width:\s*(\d+)px\)/.exec(query);
    const max = /\(max-width:\s*(\d+)px\)/.exec(query);
    let matches = true;
    if (min) matches = matches && DEFAULT_TEST_VIEWPORT_WIDTH >= Number(min[1]);
    if (max) matches = matches && DEFAULT_TEST_VIEWPORT_WIDTH <= Number(max[1]);
    return {
      matches,
      media: query,
      onchange: null,
      addEventListener: () => {},
      removeEventListener: () => {},
      addListener: () => {},
      removeListener: () => {},
      dispatchEvent: () => false,
    } as MediaQueryList;
  },
});

// Polyfill for Radix UI pointer capture (not implemented in happy-dom)
if (!Element.prototype.hasPointerCapture) {
  Element.prototype.hasPointerCapture = function () {
    return false;
  };
}

if (!Element.prototype.setPointerCapture) {
  Element.prototype.setPointerCapture = function () {};
}

if (!Element.prototype.releasePointerCapture) {
  Element.prototype.releasePointerCapture = function () {};
}

// Radix Select scrolls the active item into view on open; happy-dom has no
// layout so it ships no implementation. Stub it as a no-op.
if (!Element.prototype.scrollIntoView) {
  Element.prototype.scrollIntoView = function () {};
}

// Cleanup after each test case
afterEach(() => {
  cleanup();
});
