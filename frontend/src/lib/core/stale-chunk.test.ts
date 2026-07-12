import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { isStaleChunkError, reloadForNewVersion, initStaleChunkReload } from './stale-chunk';

vi.mock('@foundation/src/lib/core/logger', () => ({
  logger: { warn: vi.fn() },
}));

const reload = vi.fn();
const originalLocation = window.location;

beforeEach(() => {
  sessionStorage.clear();
  Object.defineProperty(window, 'location', {
    value: { reload },
    writable: true,
    configurable: true,
  });
});

afterEach(() => {
  Object.defineProperty(window, 'location', {
    value: originalLocation,
    writable: true,
    configurable: true,
  });
  vi.clearAllMocks();
});

function dispatchPreloadError(): Event {
  const event = new Event('vite:preloadError', { cancelable: true });
  (event as Event & { payload: Error }).payload = new Error('preload failed');
  window.dispatchEvent(event);
  return event;
}

describe('isStaleChunkError', () => {
  it.each([
    "'text/html' is not a valid JavaScript MIME type.",
    'Failed to fetch dynamically imported module: https://demo.orkyo.com/app/assets/Page-abc.js',
    'error loading dynamically imported module',
    'Importing a module script failed.',
  ])('recognizes chunk-load failure: %s', (message) => {
    expect(isStaleChunkError(new Error(message))).toBe(true);
  });

  it('recognizes plain-string errors', () => {
    expect(isStaleChunkError('Importing a module script failed.')).toBe(true);
  });

  it('rejects unrelated errors', () => {
    expect(isStaleChunkError(new Error('boom'))).toBe(false);
    expect(isStaleChunkError(new TypeError("Cannot read properties of undefined"))).toBe(false);
  });

  it('rejects non-error values', () => {
    expect(isStaleChunkError(undefined)).toBe(false);
    expect(isStaleChunkError(null)).toBe(false);
    expect(isStaleChunkError(42)).toBe(false);
  });
});

describe('reloadForNewVersion', () => {
  it('reloads and returns true on first call', () => {
    expect(reloadForNewVersion()).toBe(true);
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('does not reload again within the guard window', () => {
    expect(reloadForNewVersion()).toBe(true);
    expect(reloadForNewVersion()).toBe(false);
    expect(reload).toHaveBeenCalledTimes(1);
  });

  it('reloads again after the guard window has passed', () => {
    vi.useFakeTimers();
    try {
      expect(reloadForNewVersion()).toBe(true);
      vi.advanceTimersByTime(31_000);
      expect(reloadForNewVersion()).toBe(true);
      expect(reload).toHaveBeenCalledTimes(2);
    } finally {
      vi.useRealTimers();
    }
  });

  it('skips the reload when sessionStorage is unavailable', () => {
    const getItem = vi
      .spyOn(window.sessionStorage, 'getItem')
      .mockImplementation(() => {
        throw new Error('denied');
      });
    try {
      expect(reloadForNewVersion()).toBe(false);
      expect(reload).not.toHaveBeenCalled();
    } finally {
      getItem.mockRestore();
    }
  });
});

describe('initStaleChunkReload', () => {
  it('reloads and suppresses the error on vite:preloadError', () => {
    initStaleChunkReload();
    const event = dispatchPreloadError();
    expect(reload).toHaveBeenCalledTimes(1);
    expect(event.defaultPrevented).toBe(true);
  });

  it('lets the error propagate when the guard suppresses the reload', () => {
    initStaleChunkReload();
    dispatchPreloadError();
    const second = dispatchPreloadError();
    expect(reload).toHaveBeenCalledTimes(1);
    expect(second.defaultPrevented).toBe(false);
  });
});
