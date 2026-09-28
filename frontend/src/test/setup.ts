import '@testing-library/jest-dom';
import { afterEach, vi } from 'vitest';
import { cleanup, configure } from '@testing-library/react';
import type * as PermissionsModule from '@foundation/src/hooks/usePermissions';
import type * as SonnerModule from 'sonner';
import { restoreViewport } from '@foundation/src/test-utils/viewport';

// Testing Library's default asyncUtilTimeout is 1000ms, which is a wall-clock budget rather
// than a correctness bound: findBy*/waitFor still resolve the moment their condition holds,
// so a passing test is no slower for this. The default is too tight for suites that render
// lazily-loaded routes behind <Suspense> while the machine is busy — a loaded CI container
// exceeded it and failed tests that pass every time in isolation.
configure({ asyncUtilTimeout: 5000 });

// Pin the locale so USER_LOCALE (captured once at module load in lib/formatters) is deterministic
// across CI runners. Drives the 12h/24h + date formatting that the utilization grid/calendar render.
Object.defineProperty(window.navigator, 'language', { value: 'en-US', configurable: true });

// Default the permission gate to "can edit" so component tests render their write
// affordances and submit buttons enabled, as before this hook existed. Tests that exercise
// the Viewer / read-only state override with vi.mocked(useCanEdit).mockReturnValue(false).
// The hook's own test (usePermissions.test.ts) unmocks this to test the real implementation.
vi.mock('@foundation/src/hooks/usePermissions', async (importOriginal) => {
  const actual = await importOriginal<typeof PermissionsModule>();
  return { ...actual, useCanEdit: vi.fn(() => true), useIsTenantAdmin: vi.fn(() => true) };
});

// One toast double for every test: `toast` (callable) and its methods are spies, so a test
// asserts with `vi.mocked(toast.error)` instead of mocking sonner itself. The real Toaster
// stays, so a tree that mounts one still renders.
vi.mock('sonner', async (importOriginal) => {
  const actual = await importOriginal<typeof SonnerModule>();
  const toast = Object.assign(vi.fn(), {
    success: vi.fn(),
    error: vi.fn(),
    warning: vi.fn(),
    info: vi.fn(),
    message: vi.fn(),
    dismiss: vi.fn(),
  });
  return { ...actual, toast };
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

// happy-dom has no layout engine, so window.matchMedia is unreliable. Every test starts at a
// desktop width through the one width-driven fake in test-utils/viewport; a test picks another
// width with setViewport (and restores with restoreViewport).
restoreViewport();

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
