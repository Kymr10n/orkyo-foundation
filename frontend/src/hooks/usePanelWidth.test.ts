import { describe, it, expect, beforeEach, vi } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import {
  usePanelWidth,
  MIN_PANEL_WIDTH,
  MAX_PANEL_WIDTH,
  DEFAULT_PANEL_WIDTH,
} from './usePanelWidth';

const KEY = 'orkyo.test.width';

describe('usePanelWidth', () => {
  beforeEach(() => {
    localStorage.clear();
    window.innerWidth = 1600;
  });

  it('starts at the default when nothing is remembered', () => {
    const { result } = renderHook(() => usePanelWidth(KEY));

    expect(result.current.width).toBe(DEFAULT_PANEL_WIDTH);
  });

  it('remembers a width across mounts', () => {
    const { result, unmount } = renderHook(() => usePanelWidth(KEY));

    act(() => {
      result.current.onKeyDown({ key: 'ArrowLeft', shiftKey: false, preventDefault: vi.fn() } as never);
    });
    const widened = result.current.width;
    unmount();

    const { result: remounted } = renderHook(() => usePanelWidth(KEY));
    expect(remounted.current.width).toBe(widened);
  });

  it('keeps a remembered width inside the window', () => {
    // A width saved on a large monitor must not cover a laptop screen entirely.
    // 600px window: the ceiling is 520, below MAX_PANEL_WIDTH — so this exercises the
    // window clamp itself. At 900 the assertion passed on the constant alone.
    localStorage.setItem(KEY, '5000');
    window.innerWidth = 600;

    const { result } = renderHook(() => usePanelWidth(KEY));

    expect(result.current.width).toBe(520);
  });

  it('ignores a stored value that is not a width', () => {
    localStorage.setItem(KEY, 'not-a-number');

    const { result } = renderHook(() => usePanelWidth(KEY));

    expect(result.current.width).toBe(DEFAULT_PANEL_WIDTH);
  });

  it('widens on ArrowLeft, because the panel is anchored right', () => {
    const { result } = renderHook(() => usePanelWidth(KEY));
    const before = result.current.width;

    act(() => {
      result.current.onKeyDown({ key: 'ArrowLeft', shiftKey: false, preventDefault: vi.fn() } as never);
    });

    expect(result.current.width).toBe(before + 10);
  });

  it('takes a bigger step with shift held', () => {
    const { result } = renderHook(() => usePanelWidth(KEY));
    const before = result.current.width;

    act(() => {
      result.current.onKeyDown({ key: 'ArrowLeft', shiftKey: true, preventDefault: vi.fn() } as never);
    });

    expect(result.current.width).toBe(before + 50);
  });

  it('will not shrink below the readable minimum', () => {
    const { result } = renderHook(() => usePanelWidth(KEY));

    // One act per press: onKeyDown closes over the current width, so a loop inside a
    // single act would replay the same starting width forty times.
    for (let i = 0; i < 40; i++) {
      act(() => {
        result.current.onKeyDown({ key: 'ArrowRight', shiftKey: true, preventDefault: vi.fn() } as never);
      });
    }

    expect(result.current.width).toBe(MIN_PANEL_WIDTH);
  });

  it('will not grow past the maximum', () => {
    const { result } = renderHook(() => usePanelWidth(KEY));

    for (let i = 0; i < 40; i++) {
      act(() => {
        result.current.onKeyDown({ key: 'ArrowLeft', shiftKey: true, preventDefault: vi.fn() } as never);
      });
    }

    expect(result.current.width).toBe(MAX_PANEL_WIDTH);
  });

  it('survives storage being unavailable', () => {
    // Private mode: the preference is lost, the session is not.
    const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('QuotaExceededError');
    });

    const { result } = renderHook(() => usePanelWidth(KEY));
    act(() => {
      result.current.onKeyDown({ key: 'ArrowLeft', shiftKey: false, preventDefault: vi.fn() } as never);
    });

    expect(result.current.width).toBe(DEFAULT_PANEL_WIDTH + 10);
    setItem.mockRestore();
  });
});
