import React from "react";
import type { ResourceInfo } from "@foundation/src/lib/api/resources-api";
import type { ResourceUtilizationBucket } from "@foundation/src/lib/api/resource-utilization-api";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";
import {
  type BucketStatus,
  STATUS_CELL_CLASS,
} from "./schedule-colors";
import { overlapsOffTimeRange } from "./time-grid-utils";

function bucketStatus(
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
  if (bucket.allocatedPercent >= bucket.effectiveAvailabilityPercent) return "overbooked";
  return "partial";
}

export const PersonRow = React.memo(function PersonRow({
  person,
  jobTitle,
  buckets,
  isLoadingRow,
  columnCount,
  overallPct,
  offTimeRanges,
  columnLabel,
}: {
  person: ResourceInfo;
  jobTitle?: string | null;
  buckets: ResourceUtilizationBucket[];
  isLoadingRow: boolean;
  columnCount: number;
  overallPct: number;
  offTimeRanges: readonly OffTimeRange[];
  columnLabel: (date: Date) => string;
}) {
  const overallClass =
    overallPct > 100
      ? "text-red-500"
      : overallPct > 0
      ? "text-foreground"
      : "text-muted-foreground";

  return (
    <div
      className="flex border-b hover:bg-accent/30 transition-colors"
      data-testid={`person-row-${person.id}`}
    >
      {/* Label cell */}
      <div className="w-52 flex-shrink-0 px-3 py-2 border-r flex items-center gap-2">
        <div className="min-w-0 flex-1">
          <div className="font-medium text-sm truncate" title={person.name}>
            {person.name}
          </div>
          <div className="text-xs text-muted-foreground truncate" title={jobTitle ?? ""}>
            {jobTitle ?? " "}
          </div>
        </div>
        <span
          className={`text-xs font-semibold tabular-nums shrink-0 ${overallClass}`}
          title={`Overall utilization: ${overallPct}%`}
        >
          {overallPct}%
        </span>
      </div>

      {/* Bucket cells */}
      <div className="flex-1 flex">
        {isLoadingRow ? (
          <div
            className="flex-1 px-3 py-2 text-xs text-muted-foreground italic"
            style={{ minHeight: "40px" }}
          >
            Loading…
          </div>
        ) : buckets.length === 0 ? (
          <div
            className="flex-1 px-3 py-2 text-xs text-muted-foreground"
            style={{ minHeight: "40px" }}
          >
            No data
          </div>
        ) : (
          buckets.slice(0, columnCount).map((bucket, bIdx) => {
            const status = bucketStatus(bucket, person.id, offTimeRanges);
            const pct = Math.round(bucket.allocatedPercent);
            return (
              <div
                key={bIdx}
                className={`flex-1 min-w-[60px] border-r last:border-r-0 flex items-center justify-center text-xs font-medium ${STATUS_CELL_CLASS[status]}`}
                style={{ minHeight: "40px" }}
                title={`${columnLabel(new Date(bucket.start))}: ${pct}% allocated`}
                data-status={status}
              >
                {pct > 0 ? `${pct}%` : ""}
              </div>
            );
          })
        )}
      </div>
    </div>
  );
});
