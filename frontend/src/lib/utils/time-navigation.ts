import { addDays, addHours, addMinutes, addMonths, addWeeks } from "date-fns";

type TimeScale = "year" | "month" | "week" | "day" | "hour";

/** Shifts an anchor date by one step in the given direction. */
export function navigateTime(anchor: Date, scale: TimeScale, direction: 1 | -1): Date {
  switch (scale) {
    case "year":  return addMonths(anchor, direction);
    case "month": return addWeeks(anchor, direction);
    case "week":  return addDays(anchor, direction);
    case "day":   return addHours(anchor, direction);
    case "hour":  return addMinutes(anchor, direction * 15);
  }
}
