import React from "react";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";
import type { TimeColumn } from "./scheduler-types";

interface OffTimeOverlayProps {
  offTime: OffTimeRange;
  columns: TimeColumn[];
  spaceId: string;
}

/**
 * Renders a semi-transparent red overlay on the grid where an off-time
 * intersects with the visible time range for a given space.
 */
export const OffTimeOverlay = React.memo(function OffTimeOverlay({
  offTime,
  columns,
  spaceId,
}: OffTimeOverlayProps) {
  if (columns.length === 0) return null;

  // Skip if this off-time doesn't apply to this space
  if (offTime.spaceIds !== null && !offTime.spaceIds.includes(spaceId)) {
    return null;
  }

  const viewStartMs = columns[0].start.getTime();
  const viewEndMs = columns[columns.length - 1].end.getTime();
  const viewDuration = viewEndMs - viewStartMs;

  // Clamp off-time to visible range
  const clampedStart = Math.max(offTime.startMs, viewStartMs);
  const clampedEnd = Math.min(offTime.endMs, viewEndMs);

  // Skip if no overlap with view
  if (clampedStart >= clampedEnd) return null;

  const leftPercent = ((clampedStart - viewStartMs) / viewDuration) * 100;
  const widthPercent = ((clampedEnd - clampedStart) / viewDuration) * 100;

  return (
    <div
      className="absolute top-0 bottom-0 bg-destructive/15 border-l border-r border-destructive/25 pointer-events-none z-[5]"
      style={{ left: `${leftPercent}%`, width: `${widthPercent}%` }}
      title={offTime.title}
    />
  );
});
