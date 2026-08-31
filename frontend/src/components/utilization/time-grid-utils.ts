import {
  addDays,
  addHours,
  addMinutes,
  addMonths,
  addWeeks,
  isWeekend,
  startOfDay,
  startOfHour,
  startOfMonth,
  startOfWeek,
} from "date-fns";
import {
  formatCompactTime,
  formatLocalized,
  GRID_DAY_HEADER_OPTS,
  GRID_WEEK_HEADER_OPTS,
} from "@foundation/src/lib/formatters";
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

/**
 * True when the stored view anchor's calendar day is before `now` — i.e. a default `new Date()` that was
 * frozen on a prior day (a tab left open across midnight, or a long-lived HMR dev tab) and should refresh
 * to today when the board is re-opened. A same-day or future anchor is preserved.
 */
export function isAnchorStale(anchorTs: Date, now: Date): boolean {
  return startOfDay(anchorTs).getTime() < startOfDay(now).getTime();
}

/**
 * Position of an instant within a half-open `[startMs, endMs)` view range, as a 0–100 percentage for
 * absolute placement over the time track. Returns `null` when the instant is outside the range so a
 * caller (e.g. the "now" line) can hide its marker rather than clamp it to an edge.
 */
export function viewPositionPercent(tsMs: number, startMs: number, endMs: number): number | null {
  const span = endMs - startMs;
  if (span <= 0) return null;
  if (tsMs < startMs || tsMs >= endMs) return null;
  return ((tsMs - startMs) / span) * 100;
}

export function parseTimeToHour(time: string): number {
  const [hour] = time.split(":").map(Number);
  return hour;
}

/**
 * Resolve the start timestamp (ms) of a bar dragged horizontally by `deltaX` pixels.
 *
 * Movement is continuous, not quantized to column edges: the pixel delta converts
 * straight to a time delta against the view span. This is the same px → ms conversion
 * `useResizeGesture` applies, so moving and resizing by the same distance shift the bar
 * by the same amount, and the request lands exactly where the dragged bar is drawn.
 *
 * Working from the drag *delta* (not the pointer position) is what keeps the grab offset:
 * the bar translates with the pointer instead of snapping its start under it. A purely
 * vertical drag to another row therefore keeps the request's time untouched.
 *
 * The result stays inside the visible window, so a drop can never push a bar out of sight.
 * The bounds widen to include `origStartMs` when the bar already starts before the window:
 * clamping such a bar on any nudge would move a request the user only meant to pick up.
 */
export function resolveDropStartMs(
  origStartMs: number,
  durationMs: number,
  deltaX: number,
  trackWidth: number,
  viewStartMs: number,
  viewEndMs: number,
): number {
  if (trackWidth <= 0 || viewEndMs <= viewStartMs) return origStartMs;
  const deltaMs = (deltaX / trackWidth) * (viewEndMs - viewStartMs);
  const earliest = Math.min(viewStartMs, origStartMs);
  const latest = Math.max(viewStartMs, viewEndMs - durationMs, origStartMs);
  return Math.round(Math.min(Math.max(origStartMs + deltaMs, earliest), latest));
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
  // Locale-aware labels (follow USER_LOCALE) so the grid matches the calendar in every locale:
  // hour/minute share formatCompactTime with the calendar's slot-axis ("1am"/"13:00"); the day/week
  // headers share GRID_DAY/WEEK_HEADER_OPTS with the calendar's dayHeaderFormat.
  switch (granularity) {
    case "month":
      return formatLocalized(date, { month: "short", year: "2-digit" });
    case "week":
      return formatLocalized(date, GRID_WEEK_HEADER_OPTS);
    case "day":
      return formatLocalized(date, GRID_DAY_HEADER_OPTS);
    case "hour":
    case "minute":
      return formatCompactTime(date);
    default:
      return formatLocalized(date, GRID_WEEK_HEADER_OPTS);
  }
}

/** Day parts for the drop label — the column headers' weekday+day, plus the month, because
 *  the label floats over the grid instead of sitting under a header that already names it. */
const DROP_DAY_OPTS: Intl.DateTimeFormatOptions = { weekday: "short", day: "2-digit", month: "short" };

/**
 * Label for the live drop marker: the instant the dragged bar will land on, at the precision
 * the current scale lets the user aim for. A column is the unit of aim, so the finer the
 * columns, the more the label says — naming a minute on a month view would be false precision
 * (one pixel spans hours there), and naming only the day on an hour view would hide the very
 * thing being chosen.
 */
export function formatDropInstant(date: Date, scale: TimeScale): string {
  switch (scale) {
    case "hour":
      return formatCompactTime(date);
    case "day":
    case "week":
      return `${formatLocalized(date, DROP_DAY_OPTS)}, ${formatCompactTime(date)}`;
    case "month":
      return formatLocalized(date, DROP_DAY_OPTS);
    case "year":
      return formatLocalized(date, { day: "2-digit", month: "short", year: "numeric" });
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

/**
 * Whether a column is *fully* covered by an off-time range — the right test for
 * shading a whole column as off-time. A mere overlap is wrong at coarse scales:
 * every week/month column overlaps a weekend, which would paint the entire
 * grid. A column is off-time only when some range spans it end to end. Mirrors
 * the site-wide coverage check in `enrichColumnsWithOffTime`, but also honours
 * per-resource ranges.
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
