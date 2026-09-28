import { describe, it, expect } from 'vitest';
import { act, renderHook } from '@testing-library/react';
import { useAnchoredZoom } from './useAnchoredZoom';

/** A scroller 400×300 scrolled to (200, 100): its centre sits at layout point (400, 250) at 1×. */
function scroller() {
  const el = document.createElement('div');
  Object.defineProperty(el, 'clientWidth', { value: 400 });
  Object.defineProperty(el, 'clientHeight', { value: 300 });
  el.scrollLeft = 200;
  el.scrollTop = 100;
  return el;
}

describe('useAnchoredZoom', () => {
  it('steps within the limits and says when a limit is reached', () => {
    const ref = { current: scroller() };
    const { result } = renderHook(() => useAnchoredZoom(ref, { min: 0.5, max: 1, step: 0.5 }));

    expect(result.current.zoom).toBe(1);
    expect(result.current.canZoomIn).toBe(false);
    act(() => result.current.zoomIn());
    expect(result.current.zoom).toBe(1);

    act(() => result.current.zoomOut());
    expect(result.current.zoom).toBe(0.5);
    expect(result.current.canZoomOut).toBe(false);

    act(() => result.current.reset());
    expect(result.current.zoom).toBe(1);
  });

  it('keeps the point at the middle of the viewport in the middle', () => {
    const el = scroller();
    const ref = { current: el };
    const { result } = renderHook(() => useAnchoredZoom(ref));

    act(() => result.current.zoomIn());

    expect(result.current.zoom).toBe(1.25);
    // Layout point (400, 250) scaled to 1.25× is (500, 312.5); centred in 400×300.
    expect(el.scrollLeft).toBe(300);
    expect(el.scrollTop).toBe(162.5);
  });
});
