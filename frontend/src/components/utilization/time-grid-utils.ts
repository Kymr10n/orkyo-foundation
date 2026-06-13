import {
  addDays,
  addHours,
  addMinutes,
  addMonths,
  addWeeks,
  format,
  isWeekend,
  startOfDay,
  startOfHour,
  startOfMonth,
  startOfWeek,
} from "date-fns";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";
import type { TimeScale } from "./ScaleSelect";
import type { TimeColumn } from "./scheduler-types";

/**
 * Defer the backend capability validation until shortly after first paint so it doesn't compete
 * with the initial spaces/requests/floorplan fetches — conflict badges are decorative on load and
 * can appear a moment later. Used by the People grid's batch validation. (The Spaces grid now
 * sources committed conflicts from the tenant-wide registry instead.)
 */
export const CONFLICT_CHECK_DELAY_MS = 1500;

export interface WorkingHoursConfig {
  enabled: boolean;
  start: number;
  end: number;
}

/**
 * The buffered [from,to] window the grid fetches for a given scale + anchor. Wider than the
 * visible range so panning within the buffer needs no refetch; snapped to the start of the scale's
 * natural unit so navigating within that unit keeps a stable React-Query key.
 *   day ±7d · week ±4w · month ±2mo · year ±1yr (hour reuses the day window).
 */
export function getFetchWindow(scale: TimeScale, anchorTs: Date): { from: Date; to: Date } {
  switch (scale) {
    case "hour":
    case "day": {
      const base = startOfDay(anchorTs);
      return { from: addDays(base, -7), to: addDays(base, 8) };
    }
    case "week": {
      const base = startOfWeek(anchorTs, { weekStartsOn: 1 });
      return { from: addWeeks(base, -4), to: addWeeks(base, 5) };
    }
    case "month": {
      const base = startOfMonth(anchorTs);
      return { from: addMonths(base, -2), to: addMonths(base, 3) };
    }
    case "year": {
      const base = startOfMonth(anchorTs); // year view = 12 months from the anchor's month
      return { from: addMonths(base, -12), to: addMonths(base, 24) };
    }
  }
}

export function parseTimeToHour(time: string): number {
  const [hour] = time.split(":").map(Number);
  return hour;
}

/**
 * Resolve which column index the pointer is over, given the row-track's measured
 * rect. Columns are equal-width within the track, so the index is the offset
 * divided by the column width, clamped to the valid range. Shared by the drop
 * handler (→ start timestamp) and the live drop-location indicator (→ position).
 */
export function resolveColumnIndex(
  pointerX: number,
  trackLeft: number,
  trackWidth: number,
  columnCount: number,
): number {
  if (columnCount === 0 || trackWidth <= 0) return 0;
  const columnWidth = trackWidth / columnCount;
  const idx = Math.floor((pointerX - trackLeft) / columnWidth);
  return Math.min(columnCount - 1, Math.max(0, idx));
}

/**
 * Resolve the start timestamp (ms) of the column the pointer landed on. Replaces
 * the old per-cell droppable: the single row droppable carries `columnStartsMs`
 * and we compute the column here at drop time.
 */
export function resolveColumnStartMs(
  pointerX: number,
  trackLeft: number,
  trackWidth: number,
  columnStartsMs: readonly number[],
): number {
  if (columnStartsMs.length === 0) return 0;
  return columnStartsMs[resolveColumnIndex(pointerX, trackLeft, trackWidth, columnStartsMs.length)];
}

function isHourOutsideWorkingHours(hour: number, workingHours: WorkingHoursConfig | null): boolean {
  if (!workingHours?.enabled) return false;
  return hour < workingHours.start || hour >= workingHours.end;
}

export function generateTimeColumns(
  scale: TimeScale,
  anchorTs: Date,
  weekendsEnabled = false,
  workingHours: WorkingHoursConfig | null = null,
): TimeColumn[] {
  const columns: TimeColumn[] = [];

  switch (scale) {
    case "year": {
      const monthStart = startOfMonth(anchorTs);
      for (let i = 0; i < 12; i++) {
        const start = addMonths(monthStart, i);
        const end = addMonths(start, 1);
        columns.push({ start, end, label: formatTimeColumn(start, "month") });
      }
      break;
    }
    case "month": {
      const weekStart = startOfWeek(anchorTs, { weekStartsOn: 1 });
      for (let i = 0; i < 5; i++) {
        const start = addWeeks(weekStart, i);
        const end = addWeeks(start, 1);
        columns.push({ start, end, label: formatTimeColumn(start, "week") });
      }
      break;
    }
    case "week": {
      const dayStart = startOfDay(anchorTs);
      for (let i = 0; i < 7; i++) {
        const start = addDays(dayStart, i);
        const end = addDays(start, 1);
        columns.push({
          start,
          end,
          label: formatTimeColumn(start, "day"),
          isWeekend: weekendsEnabled && isWeekend(start),
        });
      }
      break;
    }
    case "day": {
      const hourStart = startOfHour(anchorTs);
      for (let i = 0; i < 24; i++) {
        const start = addHours(hourStart, i);
        const end = addHours(start, 1);
        columns.push({
          start,
          end,
          label: formatTimeColumn(start, "hour"),
          isOutsideWorkingHours: isHourOutsideWorkingHours(start.getHours(), workingHours),
        });
      }
      break;
    }
    case "hour": {
      const hourStart = startOfHour(anchorTs);
      const minuteSlot = Math.floor(anchorTs.getMinutes() / 15) * 15;
      const slotStart = new Date(hourStart);
      slotStart.setMinutes(minuteSlot);
      for (let i = 0; i < 4; i++) {
        const start = addMinutes(slotStart, i * 15);
        const end = addMinutes(start, 15);
        columns.push({
          start,
          end,
          label: formatTimeColumn(start, "minute"),
          isOutsideWorkingHours: isHourOutsideWorkingHours(start.getHours(), workingHours),
        });
      }
      break;
    }
  }

  return columns;
}

export function utilizationGranularityForScale(scale: TimeScale): string {
  switch (scale) {
    case "year":
      return "month";
    case "month":
      return "week";
    case "week":
      return "day";
    case "day":
      return "hour";
    case "hour":
      return "minute";
  }
}

export function formatTimeColumn(date: Date, granularity: string): string {
  switch (granularity) {
    case "month":
      return format(date, "MMM ''yy");
    case "week":
      return format(date, "MMM dd");
    case "day":
      return format(date, "EEE dd");
    case "hour":
      return format(date, "HH:00");
    case "minute":
      return format(date, "HH:mm");
    default:
      return format(date, "MMM dd");
  }
}

export function overlapsOffTimeRange(
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
    return offTime.startMs < endMs && offTime.endMs > startMs;
  });
}
