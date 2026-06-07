/**
 * Rendering selectors.
 *
 * Derive everything the UI needs to paint a request block from:
 *   - PreviewSchedule (position / bounds)
 *   - ScheduleIndex   (stacking)
 *   - ValidationResult (color / badge)
 *
 * All functions are pure — call them directly in render, no memoization
 * required beyond what React provides via useMemo.
 */

import type { ScheduleIndex } from "./schedule-index";
import { getOverlapGroupSize, getStackIndex, getMaxOverlapInSpace } from "./schedule-index";
import type { PreviewEntry, ValidationResult } from "./schedule-model";
import { hasConflicts } from "./schedule-validator";

// ---------------------------------------------------------------------------
// Row-level selectors
// ---------------------------------------------------------------------------

/** How many rows tall should a space row be? (count of max simultaneous overlaps) */
export function selectSpaceOverlapCount(index: ScheduleIndex, resourceId: string): number {
  return getMaxOverlapInSpace(index, resourceId);
}

// ---------------------------------------------------------------------------
// Request-block selectors
// ---------------------------------------------------------------------------

interface RequestDisplayData {
  /** 0–100 percentage left offset within the visible time range */
  leftPercent: number;
  /** 0–100 percentage width within the visible time range */
  widthPercent: number;
  /** Pixel offset from top of the space row */
  topPx: number;
  /** Pixel height of the block */
  heightPx: number;
  /** z-index for stacking */
  zIndex: number;
  /** Whether any conflict exists for this request */
  hasConflict: boolean;
  /** Whether the block is currently in draft (being resized) */
  isDraft: boolean;
}

const BASE_ROW_HEIGHT_PX = 52; // matches SpaceRow BASE_ROW_HEIGHT
const REQUEST_HEIGHT_PX = 44; // matches SchedulerGrid constant
const REQUEST_INNER_HEIGHT_PX = REQUEST_HEIGHT_PX - 8;
const BASE_TOP_PAD_PX = (BASE_ROW_HEIGHT_PX - REQUEST_INNER_HEIGHT_PX) / 2;

/**
 * Clamp a [start, end] interval to the visible time window and express its
 * horizontal placement as left/width percentages. Shared by request bars
 * (Spaces) and utilization segments (People) so the timeline math stays in one
 * place. Width is 0 when the interval is entirely outside the window.
 */
export function clampToViewPercent(
  startMs: number,
  endMs: number,
  viewStartMs: number,
  viewEndMs: number,
): { leftPercent: number; widthPercent: number } {
  const viewDuration = viewEndMs - viewStartMs;
  const visibleStart = Math.max(startMs, viewStartMs);
  const visibleEnd = Math.min(endMs, viewEndMs);
  const visibleDuration = Math.max(0, visibleEnd - visibleStart);

  const offsetFromStart = Math.max(0, startMs - viewStartMs);
  return {
    leftPercent: (offsetFromStart / viewDuration) * 100,
    widthPercent: (visibleDuration / viewDuration) * 100,
  };
}

/**
 * Compute everything needed to render a request block.
 *
 * @param entry        The preview entry for this request
 * @param index        The index built from the current preview schedule
 * @param validation   The validation result for the current preview
 * @param viewStartMs  Start of the visible time window (epoch ms)
 * @param viewEndMs    End of the visible time window (epoch ms)
 */
export function selectRequestDisplayData(
  entry: PreviewEntry,
  index: ScheduleIndex,
  validation: ValidationResult,
  viewStartMs: number,
  viewEndMs: number
): RequestDisplayData {
  const { leftPercent, widthPercent } = clampToViewPercent(
    entry.startMs,
    entry.endMs,
    viewStartMs,
    viewEndMs,
  );

  const stackIndex = getStackIndex(index, entry);
  const groupSize = getOverlapGroupSize(index, entry);
  const hasOverlap = groupSize > 1;

  const topPx = BASE_TOP_PAD_PX + stackIndex * REQUEST_HEIGHT_PX;
  const heightPx = REQUEST_INNER_HEIGHT_PX;
  const zIndex = hasOverlap ? 10 + stackIndex : 10;

  return {
    leftPercent,
    widthPercent,
    topPx,
    heightPx,
    zIndex,
    hasConflict: hasConflicts(validation, entry.requestId),
    isDraft: entry.isDraft,
  };
}

/**
 * Returns true if the entry is entirely outside the visible window.
 * Use this to skip rendering off-screen blocks.
 */
export function isOutsideView(
  entry: PreviewEntry,
  viewStartMs: number,
  viewEndMs: number
): boolean {
  return entry.endMs <= viewStartMs || entry.startMs >= viewEndMs;
}
