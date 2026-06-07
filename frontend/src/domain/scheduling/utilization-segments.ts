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
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";
import type { BucketStatus } from "@foundation/src/components/utilization/schedule-colors";
import { overlapsOffTimeRange } from "@foundation/src/components/utilization/time-grid-utils";
import { clampToViewPercent } from "@foundation/src/domain/scheduling/schedule-selectors";

export interface PersonUtilizationSegment {
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
 * Order matters: non-working (no effective availability, or an off-time
 * overlap) wins first; then exclusive occupation; then the fractional bands.
 */
export function deriveBucketStatus(
  bucket: ResourceUtilizationBucket,
  resourceId: string,
  offTimeRanges: readonly OffTimeRange[],
): BucketStatus {
  if (bucket.effectiveAvailabilityPercent === 0) return "non-working";
  if (
    overlapsOffTimeRange(
      resourceId,
      new Date(bucket.start).getTime(),
      new Date(bucket.end).getTime(),
      offTimeRanges,
    )
  ) {
    return "non-working";
  }
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
  resourceId: string,
  offTimeRanges: readonly OffTimeRange[],
): PersonUtilizationSegment[] {
  const segments: PersonUtilizationSegment[] = [];

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
    const status = deriveBucketStatus(bucket, resourceId, offTimeRanges);
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
  segment: PersonUtilizationSegment,
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
