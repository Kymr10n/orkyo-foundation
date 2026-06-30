import { viewPositionPercent } from "./time-grid-utils";

/**
 * Fixed "now" marker for the Spaces scheduler — a non-draggable vertical line at the current
 * wall-clock time, visually distinct from the blue draggable time scrubber.
 *
 * Its job is to make clear which instant the clock-derived "In Progress" status refers to: the
 * request whose bar this line crosses is the one running right now. It is driven by the SAME ticking
 * `nowMs` clock (`useNow`) that the operational status recompute uses, so the line and the backlog
 * panel's "In Progress" filter are always evaluated against one instant and can't drift apart. Renders
 * nothing when "now" is outside the visible range — it never clamps to an edge.
 */
export function NowLine({
  nowMs,
  viewStartMs,
  viewEndMs,
}: {
  nowMs: number;
  viewStartMs: number;
  viewEndMs: number;
}) {
  const pct = viewPositionPercent(nowMs, viewStartMs, viewEndMs);
  if (pct === null) return null;

  return (
    // Same offset as the cursor container so the percentage lines up with the time columns.
    <div className="absolute top-0 bottom-0 left-52 right-0 pointer-events-none z-30">
      <div
        className="absolute top-0 bottom-0 w-0.5 bg-rose-500"
        style={{ left: `${pct}%` }}
        data-testid="now-line"
      >
        <span className="absolute top-0 left-1/2 -translate-x-1/2 rounded-sm bg-rose-500 px-1 text-[10px] font-medium leading-tight text-white">
          Now
        </span>
      </div>
    </div>
  );
}
