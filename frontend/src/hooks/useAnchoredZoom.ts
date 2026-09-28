import { useCallback, useLayoutEffect, useRef, useState, type RefObject } from "react";

export interface ZoomLimits {
  min?: number;
  max?: number;
  step?: number;
}

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
export function useAnchoredZoom(
  scrollRef: RefObject<HTMLElement | null>,
  { min = 0.5, max = 2, step = 0.25 }: ZoomLimits = {},
): AnchoredZoom {
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
    zoomIn: () => applyZoom(Math.min(max, zoom + step)),
    zoomOut: () => applyZoom(Math.max(min, zoom - step)),
    reset: () => applyZoom(1),
    canZoomIn: zoom < max,
    canZoomOut: zoom > min,
  };
}
