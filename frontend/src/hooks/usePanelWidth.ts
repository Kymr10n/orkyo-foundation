import { useCallback, useEffect, useRef, useState } from "react";
import { logger } from "@foundation/src/lib/core/logger";

/** Narrower than this and the conversation is unreadable; wider and it swallows the page. */
export const MIN_PANEL_WIDTH = 320;
export const MAX_PANEL_WIDTH = 720;
export const DEFAULT_PANEL_WIDTH = 448; // what `sm:max-w-md` gave before this was resizable

/**
 * A width the person can drag, remembered on this device.
 *
 * Width is a property of the screen someone is sitting at, not of their account, so it
 * stays in localStorage rather than travelling with them the way conversations do.
 *
 * @param storageKey namespaced `orkyo.*`, following the convention in `useTypeFilter`.
 */
export function usePanelWidth(storageKey: string) {
  const [width, setWidth] = useState<number>(() => read(storageKey));
  const [isDragging, setIsDragging] = useState(false);
  const dragStartX = useRef(0);
  const dragStartWidth = useRef(0);
  /**
   * The width the drag has reached. Written from the move handler rather than during
   * render, so the end handler can persist the final value without this effect
   * re-subscribing on every pixel.
   */
  const latestWidth = useRef(width);

  /** Moves the edge. Cheap enough to run on every pointer event. */
  const apply = useCallback((next: number) => setWidth(clamp(next)), []);

  /**
   * Writes the preference down. Separate from {@link apply} because a drag emits dozens
   * of moves a second: persisting each one is needless synchronous work, and in private
   * mode each failure would log, turning one drag into hundreds of error lines.
   */
  const remember = useCallback(
    (value: number) => {
      try {
        localStorage.setItem(storageKey, String(value));
      } catch (err) {
        // Losing the preference is acceptable; losing the session is not.
        logger.error("Could not remember the panel width", err);
      }
    },
    [storageKey],
  );

  const onPointerDown = useCallback(
    (e: React.PointerEvent) => {
      setIsDragging(true);
      dragStartX.current = e.clientX;
      dragStartWidth.current = width;
      latestWidth.current = width;
      // currentTarget, not target: hitting the grip icon would otherwise capture on the
      // SVG child, and the capture would be lost the moment the pointer left it.
      e.currentTarget.setPointerCapture(e.pointerId);
      e.preventDefault();
    },
    [width],
  );

  useEffect(() => {
    if (!isDragging) return;

    const onMove = (e: PointerEvent) => {
      // The panel is anchored right, so dragging left must widen it: the delta inverts.
      const next = clamp(dragStartWidth.current - (e.clientX - dragStartX.current));
      latestWidth.current = next;
      setWidth(next);
    };
    const onEnd = () => {
      setIsDragging(false);
      // The drag is over: this is the width worth keeping.
      remember(latestWidth.current);
    };

    document.addEventListener("pointermove", onMove);
    document.addEventListener("pointerup", onEnd);
    // A cancelled touch (a system gesture taking over) never sends pointerup, and without
    // this the panel would stay in drag mode for good.
    document.addEventListener("pointercancel", onEnd);
    return () => {
      document.removeEventListener("pointermove", onMove);
      document.removeEventListener("pointerup", onEnd);
      document.removeEventListener("pointercancel", onEnd);
    };
  }, [isDragging, remember]);

  const onKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      const step = e.shiftKey ? 50 : 10;
      // Key presses are discrete, so each one is worth remembering as it happens.
      if (e.key === "ArrowLeft") {
        e.preventDefault();
        const next = clamp(width + step);
        apply(next);
        latestWidth.current = next;
        remember(next);
      } else if (e.key === "ArrowRight") {
        e.preventDefault();
        const next = clamp(width - step);
        apply(next);
        latestWidth.current = next;
        remember(next);
      }
    },
    [width, apply, remember],
  );

  return { width, isDragging, onPointerDown, onKeyDown };
}

function clamp(value: number): number {
  // Never wider than the window: a remembered width from a large monitor must not cover
  // a laptop screen entirely.
  const ceiling =
    typeof window === "undefined"
      ? MAX_PANEL_WIDTH
      : Math.min(MAX_PANEL_WIDTH, Math.max(MIN_PANEL_WIDTH, window.innerWidth - 80));
  return Math.max(MIN_PANEL_WIDTH, Math.min(ceiling, Math.round(value)));
}

function read(storageKey: string): number {
  try {
    const stored = localStorage.getItem(storageKey);
    if (!stored) return DEFAULT_PANEL_WIDTH;
    const parsed = Number.parseInt(stored, 10);
    return Number.isFinite(parsed) ? clamp(parsed) : DEFAULT_PANEL_WIDTH;
  } catch {
    return DEFAULT_PANEL_WIDTH;
  }
}
