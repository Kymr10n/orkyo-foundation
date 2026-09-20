/**
 * People utilization timeline segments.
 *
 * Pure domain logic that turns the per-bucket utilization series returned by
 * `GET /api/utilization` into merged, render-ready status segments. Consecutive
 * buckets that share the same status AND are contiguous collapse into a single
 * segment (the People-tab analogue of a Spaces request bar).
 *
 * This module is intentionally framework-free: it imports only types + the
 * off-time overlap helper, so it can be unit-tested in isolation. Status
 * derivation lives here (not in the row component) so the heatmap-era logic has
 * a single, tested home.
 */

import type { ResourceUtilizationBucket } from "@foundation/src/lib/api/resource-utilization-api";
import type { BucketStatus } from "@foundation/src/domain/scheduling/types";
import { clampToViewPercent } from "@foundation/src/domain/scheduling/schedule-selectors";

export interface ResourceUtilizationSegment {
  /** ISO start of the first merged bucket (inclusive). */
  start: string;
  /** ISO end of the last merged bucket (exclusive). */
  end: string;
  status: BucketStatus;
  /** Representative allocation — rounded average across the merged buckets. */
  utilizationPercent: number;
  /** How many source buckets merged into this segment. */
  sourceUnitCount: number;
}

/**
 * Derive the aggregated status for a single utilization bucket.
 *
 * Order matters: non-working wins first; then exclusive occupation; then the
 * fractional bands.
 *
 * `effectiveAvailabilityPercent` is the only input for "was this workable", and
 * deliberately so. The backend already masks weekends and working hours out of
 * both sides of the allocation ratio (`SchedulingEngine.WorkingMinutesInWindow`
 * is its denominator), so a Monday-to-Friday booking in a week bucket arrives as
 * 100. This function used to ALSO test the off-time ranges the page draws with,
 * which re-derived the same weekend rule client-side — and did it with an
 * overlap test, so at Month scale every week bucket touched a Saturday and the
 * whole grid read "Off" at 0%. One side owns the question now.
 */
export function deriveBucketStatus(bucket: ResourceUtilizationBucket): BucketStatus {
  if (bucket.effectiveAvailabilityPercent === 0) return "non-working";
  if (bucket.isExclusiveOccupied) return "assigned";
  if (bucket.allocatedPercent === 0) return "available";
  // Strictly greater than capacity = overbooked. Exactly at capacity (e.g. a
  // person booked 100% of a 100% availability) is fully booked, not over.
  if (bucket.allocatedPercent > bucket.effectiveAvailabilityPercent) return "overbooked";
  return "partial";
}

/**
 * Merge an ordered bucket series into status segments.
 *
 * A run extends only while the next bucket has the SAME status AND is
 * contiguous with the run (its `start` equals the run's current `end`, compared
 * as exact server strings — DST-safe, no local re-derivation). A status change
 * or a time gap closes the run and opens a new one.
 */
export function mergeBucketsToSegments(
  buckets: readonly ResourceUtilizationBucket[],
): ResourceUtilizationSegment[] {
  const segments: ResourceUtilizationSegment[] = [];

  let runStart: string | null = null;
  let runEnd = "";
  let runStatus: BucketStatus | null = null;
  let allocatedSum = 0;
  let count = 0;

  const closeRun = () => {
    if (runStart === null || runStatus === null) return;
    segments.push({
      start: runStart,
      end: runEnd,
      status: runStatus,
      utilizationPercent: Math.round(allocatedSum / count),
      sourceUnitCount: count,
    });
  };

  for (const bucket of buckets) {
    const status = deriveBucketStatus(bucket);
    const contiguous = runStatus === status && runEnd === bucket.start;

    if (runStart !== null && contiguous) {
      runEnd = bucket.end;
      allocatedSum += bucket.allocatedPercent;
      count += 1;
      continue;
    }

    closeRun();
    runStart = bucket.start;
    runEnd = bucket.end;
    runStatus = status;
    allocatedSum = bucket.allocatedPercent;
    count = 1;
  }
  closeRun();

  return segments;
}

/**
 * Position a segment within the visible window as left/width percentages.
 * Reuses the same clamp math as the Spaces request bars; segments never stack,
 * so there is no top/z handling here.
 */
export function segmentDisplayData(
  segment: ResourceUtilizationSegment,
  viewStartMs: number,
  viewEndMs: number,
): { leftPercent: number; widthPercent: number } {
  return clampToViewPercent(
    new Date(segment.start).getTime(),
    new Date(segment.end).getTime(),
    viewStartMs,
    viewEndMs,
  );
}
