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

export interface WorkingHoursConfig {
  enabled: boolean;
  start: number;
  end: number;
}

export function parseTimeToHour(time: string): number {
  const [hour] = time.split(":").map(Number);
  return hour;
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
