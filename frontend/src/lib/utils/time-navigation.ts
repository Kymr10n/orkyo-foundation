import { addDays, addMonths, addWeeks } from "date-fns";

type TimeScale = "year" | "month" | "week" | "day" | "hour";

/**
 * Shifts an anchor by one whole period of the FullCalendar view that `scale`
 * maps to (see scaleToCalendarView): day/hour → day, week → week, month/year →
 * month. Every time control pages this way — one click, one period — so a week
 * view moves to the next week rather than needing seven clicks.
 */
export function navigateCalendarPeriod(anchor: Date, scale: TimeScale, direction: 1 | -1): Date {
  switch (scale) {
    case "day":
    case "hour":  return addDays(anchor, direction);
    case "week":  return addWeeks(anchor, direction);
    default:      return addMonths(anchor, direction); // month / year
  }
}
