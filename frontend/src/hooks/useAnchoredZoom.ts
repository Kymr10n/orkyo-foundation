import { useCallback, useLayoutEffect, useRef, useState, type RefObject } from "react";

/** The zoom range and the size of one button step. */
export const ZOOM_MIN = 0.5;
export const ZOOM_MAX = 2;
export const ZOOM_STEP = 0.25;

export interface AnchoredZoom {
  zoom: number;
  zoomIn: () => void;
  zoomOut: () => void;
  reset: () => void;
  canZoomIn: boolean;
  canZoomOut: boolean;
}

/**
 * Button zoom for a scaled surface inside a scroll container, kept centred on what the reader
 * was looking at. A CSS scale grows from the top-left corner, so without a correction the
 * viewport lands somewhere else entirely on a tall plan; the scroll offset is moved on purpose,
 * in a layout effect, before the new scale paints.
 */
export function useAnchoredZoom(scrollRef: RefObject<HTMLElement | null>): AnchoredZoom {
  const [zoom, setZoom] = useState(1);
  const anchor = useRef<{ x: number; y: number } | null>(null);

  const applyZoom = useCallback(
    (next: number) => {
      const el = scrollRef.current;
      // Layout-space point currently at the middle of the viewport, to be put back there after.
      if (el) {
        anchor.current = {
          x: (el.scrollLeft + el.clientWidth / 2) / zoom,
          y: (el.scrollTop + el.clientHeight / 2) / zoom,
        };
      }
      setZoom(next);
    },
    [scrollRef, zoom],
  );

  useLayoutEffect(() => {
    const el = scrollRef.current;
    const point = anchor.current;
    anchor.current = null;
    if (!el || !point) return;
    el.scrollLeft = point.x * zoom - el.clientWidth / 2;
    el.scrollTop = point.y * zoom - el.clientHeight / 2;
  }, [scrollRef, zoom]);

  return {
    zoom,
    zoomIn: () => applyZoom(Math.min(ZOOM_MAX, zoom + ZOOM_STEP)),
    zoomOut: () => applyZoom(Math.max(ZOOM_MIN, zoom - ZOOM_STEP)),
    reset: () => applyZoom(1),
    canZoomIn: zoom < ZOOM_MAX,
    canZoomOut: zoom > ZOOM_MIN,
  };
}
