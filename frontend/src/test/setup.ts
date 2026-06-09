import '@testing-library/jest-dom';
import { afterEach } from 'vitest';
import { cleanup } from '@testing-library/react';

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

// Cleanup after each test case
afterEach(() => {
  cleanup();
});
