/**
 * Off-time range predicate.
 *
 * One question, asked by the two places that shade grid cells: is this column wholly closed?
 *
 * There used to be a second predicate here, `overlapsOffTimeRange`, testing mere intersection.
 * Three callers had to pick between them and two picked wrong, which painted every column as
 * off at Month and Year scale because a week or a month always contains a Saturday. Overlap is
 * not the right question at any scale — a five-minute closure does not close an hour — so the
 * predicate is gone rather than documented. Utilization status and percentages no longer ask
 * about off-time at all; the backend folds it into the availability it reports.
 */

import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";

/**
 * Whether a column is *fully* covered by an off-time range — the right test for
 * shading a whole column as off-time. A column is off-time only when some range
 * spans it end to end. `enrichColumnsWithOffTime` calls this for the site-wide
 * subset; this signature also honours per-resource ranges.
 */
export function coversOffTimeRange(
  resourceId: string,
  startMs: number,
  endMs: number,
  offTimeRanges: readonly OffTimeRange[],
): boolean {
  if (offTimeRanges.length === 0) return false;
  return offTimeRanges.some((offTime) => {
    if (offTime.resourceIds !== null && !offTime.resourceIds.includes(resourceId)) {
      return false;
    }
    return offTime.startMs <= startMs && offTime.endMs >= endMs;
  });
}
