import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";
import type { TimeColumn } from "./scheduler-types";

/**
 * Marks columns fully covered by a site-wide off-time range (a holiday or
 * closure that applies to every resource — `resourceIds === null`) with
 * `isGlobalOffTime`. Used by both the Spaces and People grids so holiday /
 * closure tinting is identical across them.
 *
 * Returns the same array reference when nothing is site-wide, so callers can
 * keep it inside a `useMemo` without churning identity.
 */
export function enrichColumnsWithOffTime(
  columns: readonly TimeColumn[],
  offTimeRanges: readonly OffTimeRange[],
): TimeColumn[] {
  const siteWide = offTimeRanges.filter((r) => r.resourceIds === null);
  if (siteWide.length === 0) return columns as TimeColumn[];
  return columns.map((col) => {
    const colStartMs = col.start.getTime();
    const colEndMs = col.end.getTime();
    const isGlobalOffTime = siteWide.some(
      (r) => r.startMs <= colStartMs && r.endMs >= colEndMs,
    );
    return isGlobalOffTime ? { ...col, isGlobalOffTime: true } : col;
  });
}
