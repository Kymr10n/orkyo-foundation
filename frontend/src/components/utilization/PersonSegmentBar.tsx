import React, { useMemo } from "react";
import { AlertTriangle, Briefcase } from "lucide-react";
import type { PersonUtilizationSegment } from "@foundation/src/domain/scheduling/utilization-segments";
import { segmentDisplayData } from "@foundation/src/domain/scheduling/utilization-segments";
import type { BucketStatus } from "./schedule-colors";
import { STATUS_CELL_CLASS, STATUS_BORDER_CLASS, STATUS_FILL_CLASS } from "./schedule-colors";
import { formatLocalized, HOUR_CYCLE } from "@foundation/src/lib/formatters";

/** Datetime shown in segment tooltips/aria — locale date + 24h house time. */
const SEGMENT_DATETIME_OPTS: Intl.DateTimeFormatOptions = {
  dateStyle: "medium",
  timeStyle: "short",
  hourCycle: HOUR_CYCLE,
};

/** Below this width the inline label is hidden; detail moves to the tooltip. */
const LABEL_MIN_WIDTH_PERCENT = 6;

const STATUS_TEXT: Record<BucketStatus, string> = {
  available: "Available",
  partial: "Booked",
  assigned: "Assigned",
  overbooked: "Overbooked",
  "non-working": "Off",
};

/** Statuses whose label carries the utilization figure (e.g. "Booked 65%"). */
const STATUS_SHOWS_PERCENT: ReadonlySet<BucketStatus> = new Set(["partial", "overbooked"]);

/** Human label shown inside the bar, e.g. "Booked 65%" / "Off". */
function segmentLabel(segment: PersonUtilizationSegment): string {
  const base = STATUS_TEXT[segment.status];
  return STATUS_SHOWS_PERCENT.has(segment.status)
    ? `${base} ${segment.utilizationPercent}%`
    : base;
}

/**
 * Width (0–100) of the solid meter fill painted left-to-right over the track.
 * Booked segments fill to their actual allocation; assigned/overbooked read as
 * fully taken (overbooked is capped at 100 and coloured red); available and
 * non-working show no fill — their track tint alone carries the meaning.
 */
function fillPercent(segment: PersonUtilizationSegment): number {
  switch (segment.status) {
    case "partial":
      return Math.min(100, Math.max(0, segment.utilizationPercent));
    case "assigned":
    case "overbooked":
      return 100;
    default:
      return 0;
  }
}

/**
 * A single read-only utilization segment in a person's timeline row.
 *
 * Unlike the Spaces request bars, segments are NOT draggable or resizable —
 * they aggregate booking status. Clicking (or Enter/Space) opens the person
 * assignment dialog for the segment's period.
 */
export const PersonSegmentBar = React.memo(function PersonSegmentBar({
  segment,
  personName,
  viewStartMs,
  viewEndMs,
  assignmentCount = 0,
  hasConflict = false,
  onClick,
}: {
  segment: PersonUtilizationSegment;
  personName: string;
  viewStartMs: number;
  viewEndMs: number;
  assignmentCount?: number;
  /** True when one or more assignments in this segment have a capability mismatch. */
  hasConflict?: boolean;
  onClick: (segment: PersonUtilizationSegment) => void;
}) {
  const { leftPercent, widthPercent } = segmentDisplayData(segment, viewStartMs, viewEndMs);

  const label = segmentLabel(segment);
  const showCount = assignmentCount > 0;

  // Datetime formatting is comparatively expensive; memoize the tooltip/aria
  // strings so they aren't rebuilt on re-renders where the inputs are unchanged.
  // Declared before the early return below to satisfy the rules of hooks.
  const { tooltip, ariaLabel } = useMemo(() => {
    const period = `${formatLocalized(new Date(segment.start), SEGMENT_DATETIME_OPTS)} – ${formatLocalized(new Date(segment.end), SEGMENT_DATETIME_OPTS)}`;
    return {
      tooltip: `${personName} · ${label}${showCount ? ` · ${assignmentCount} assignment${assignmentCount === 1 ? "" : "s"}` : ""}${hasConflict ? " · ⚠ capability conflict" : ""} · ${period}`,
      ariaLabel: `${personName}, ${label}, ${period}. Open assignment dialog.`,
    };
  }, [segment, personName, label, showCount, assignmentCount, hasConflict]);

  // Fully outside the visible window — nothing to paint.
  if (widthPercent <= 0) return null;

  const showLabel = widthPercent >= LABEL_MIN_WIDTH_PERCENT;
  const fill = fillPercent(segment);
  const fillClass = STATUS_FILL_CLASS[segment.status];

  const activate = () => onClick(segment);

  return (
    <div
      role="button"
      tabIndex={0}
      aria-label={ariaLabel}
      title={tooltip}
      data-status={segment.status}
      data-testid="person-segment-bar"
      onClick={activate}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          activate();
        }
      }}
      className={`absolute top-2 bottom-2 rounded border flex items-center gap-1.5 overflow-hidden px-2 text-xs font-medium cursor-pointer transition hover:brightness-95 outline-hidden focus-visible:ring-2 focus-visible:ring-ring ${STATUS_CELL_CLASS[segment.status]} ${STATUS_BORDER_CLASS[segment.status]}`}
      style={{ left: `${leftPercent}%`, width: `${widthPercent}%` }}
    >
      {/* Solid utilization meter — fills the track left-to-right. */}
      {fill > 0 && fillClass && (
        <div
          className={`absolute inset-y-0 left-0 ${fillClass}`}
          style={{ width: `${fill}%` }}
          data-testid="segment-fill"
          aria-hidden="true"
        />
      )}
      {showLabel && <span className="relative z-10 truncate">{label}</span>}
      {showLabel && (showCount || hasConflict) && (
        <span className="relative z-10 ml-auto flex shrink-0 items-center gap-1">
          {showCount && (
            <span
              className="flex items-center gap-1 rounded-sm bg-foreground/10 px-1.5 py-0.5 text-[11px] font-semibold tabular-nums"
              data-testid="assignment-count-badge"
            >
              <Briefcase className="h-3 w-3 opacity-70" />
              {assignmentCount}
            </span>
          )}
          {hasConflict && (
            <span
              className="flex items-center rounded-sm bg-amber-500/20 px-1 py-0.5 text-amber-600 dark:text-amber-400"
              data-testid="capability-conflict-badge"
            >
              <AlertTriangle className="h-3 w-3" />
            </span>
          )}
        </span>
      )}
    </div>
  );
});
