/**
 * useResizeGesture — pointer lifecycle hook for horizontal resize gestures.
 *
 * Handles the full down → move → up cycle with document-level native event
 * listeners (avoids React synthetic event delegation issues with pointer
 * capture on small target elements).
 *
 * Listeners are registered **synchronously** in beginGesture and removed
 * synchronously in the up/cancel handler.  This eliminates the timing gap
 * that exists with useState + useEffect (where listeners only appear after
 * the next render) which can miss fast pointer events.
 *
 * All callbacks are accessed via a ref so closures are never stale.
 */

import { useCallback, useEffect, useRef } from "react";
import type { ResizeEdge } from "@/domain/scheduling/schedule-model";

// ---------------------------------------------------------------------------
// Types
// ---------------------------------------------------------------------------

export interface ResizeGestureResult {
  startMs: number;
  endMs: number;
}

export interface ResizeGeometry {
  origStartMs: number;
  origEndMs: number;
  /** Width in pixels of the time-axis container (for px → ms conversion). */
  containerWidthPx: number;
  /** Total visible time range in ms. */
  totalDurationMs: number;
}

export interface ResizeGestureCallbacks {
  /** Called once when the pointer first exceeds the activation threshold. */
  onStart: (edge: ResizeEdge) => void;
  /** Called on every pointer move after activation. */
  onUpdate: (startMs: number, endMs: number) => void;
  /** Called on pointer up after a meaningful drag. */
  onCommit: (result: ResizeGestureResult) => void;
  /** Called on pointer up with no meaningful movement. */
  onCancel: () => void;
}

interface UseResizeGestureOptions {
  /** Minimum pixel delta before a gesture activates. */
  thresholdPx: number;
  /** Absolute floor on the resulting duration (ms). */
  minDurationMs: number;
}

// ---------------------------------------------------------------------------
// Hook
// ---------------------------------------------------------------------------

export function useResizeGesture(
  callbacks: ResizeGestureCallbacks,
  options: UseResizeGestureOptions,
) {
  // Mutable gesture state — never triggers re-renders.
  const gestureRef = useRef<{
    edge: ResizeEdge;
    origStartMs: number;
    origEndMs: number;
    startX: number;
    containerWidthPx: number;
    totalDurationMs: number;
    activated: boolean;
    latestStartMs: number;
    latestEndMs: number;
  } | null>(null);

  // Ref-stable callbacks so closures are never stale.
  const cbRef = useRef(callbacks);
  cbRef.current = callbacks;

  // Options are expected to be constants but we ref them for safety.
  const optsRef = useRef(options);
  optsRef.current = options;

  /** Timestamp of the last completed resize — exposed for click suppression. */
  const lastCommitMsRef = useRef(0);

  /** Currently registered document listeners (null when idle). */
  const listenersRef = useRef<{
    move: (e: PointerEvent) => void;
    end: () => void;
  } | null>(null);

  // -------------------------------------------------------------------------
  // Teardown — removes document listeners and resets gesture state.
  // -------------------------------------------------------------------------

  const teardown = useCallback(() => {
    if (listenersRef.current) {
      document.removeEventListener("pointermove", listenersRef.current.move);
      document.removeEventListener("pointerup", listenersRef.current.end);
      document.removeEventListener("pointercancel", listenersRef.current.end);
      listenersRef.current = null;
    }
    gestureRef.current = null;
  }, []);

  // Safety: clean up on unmount in case a gesture is in-flight.
  useEffect(() => teardown, [teardown]);

  // -------------------------------------------------------------------------
  // Pointer-down handler (attached to the resize handle via React)
  // -------------------------------------------------------------------------

  const beginGesture = useCallback(
    (e: React.PointerEvent, edge: ResizeEdge, geometry: ResizeGeometry) => {
      e.stopPropagation();
      e.nativeEvent.stopPropagation();
      e.preventDefault();

      // Clean up any leftover gesture (shouldn't happen, but be defensive).
      teardown();

      gestureRef.current = {
        edge,
        origStartMs: geometry.origStartMs,
        origEndMs: geometry.origEndMs,
        startX: e.clientX,
        containerWidthPx: geometry.containerWidthPx,
        totalDurationMs: geometry.totalDurationMs,
        activated: false,
        latestStartMs: geometry.origStartMs,
        latestEndMs: geometry.origEndMs,
      };

      // -- Handlers closed over refs — stable for the lifetime of the gesture. --

      const handlePointerMove = (ev: PointerEvent) => {
        const g = gestureRef.current;
        if (!g) return;

        const deltaX = ev.clientX - g.startX;

        // First meaningful movement → activate
        if (!g.activated && Math.abs(deltaX) >= optsRef.current.thresholdPx) {
          g.activated = true;
          cbRef.current.onStart(g.edge);
        }
        if (!g.activated) return;

        const deltaMs = (deltaX / g.containerWidthPx) * g.totalDurationMs;
        let newStartMs = g.origStartMs;
        let newEndMs = g.origEndMs;

        if (g.edge === "right") {
          newEndMs = Math.max(
            g.origEndMs + deltaMs,
            g.origStartMs + optsRef.current.minDurationMs,
          );
        } else {
          newStartMs = Math.min(
            g.origStartMs + deltaMs,
            g.origEndMs - optsRef.current.minDurationMs,
          );
        }

        g.latestStartMs = newStartMs;
        g.latestEndMs = newEndMs;
        cbRef.current.onUpdate(newStartMs, newEndMs);
      };

      const handlePointerEnd = () => {
        const g = gestureRef.current;

        if (g?.activated) {
          lastCommitMsRef.current = Date.now();
          cbRef.current.onCommit({
            startMs: g.latestStartMs,
            endMs: g.latestEndMs,
          });
        } else {
          cbRef.current.onCancel();
        }

        teardown();
      };

      // Register listeners immediately — no render gap.
      listenersRef.current = { move: handlePointerMove, end: handlePointerEnd };
      document.addEventListener("pointermove", handlePointerMove);
      document.addEventListener("pointerup", handlePointerEnd);
      document.addEventListener("pointercancel", handlePointerEnd);
    },
    [teardown],
  );

  return { beginGesture, lastCommitMsRef };
}
