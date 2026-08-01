import { addDays, addHours, addMinutes, addMonths, addWeeks } from "date-fns";

type TimeScale = "year" | "month" | "week" | "day" | "hour";

/**
 * Shifts an anchor by one sub-period step — used by the timeline **grid**, which
 * pans smoothly (a week grid slides day-by-day, a month grid week-by-week).
 */
export function navigateTime(anchor: Date, scale: TimeScale, direction: 1 | -1): Date {
  switch (scale) {
    case "year":  return addMonths(anchor, direction);
    case "month": return addWeeks(anchor, direction);
    case "week":  return addDays(anchor, direction);
    case "day":   return addHours(anchor, direction);
    case "hour":  return addMinutes(anchor, direction * 15);
  }
}

/**
 * Shifts an anchor by one whole period of the FullCalendar view that `scale`
 * maps to (see scaleToCalendarView): day/hour → day, week → week, month/year →
 * month. The **calendar** pages by full periods (one click = one week/month),
 * unlike the grid's sub-period pan above — otherwise a week view would only move
 * every 7th click.
 */
export function navigateCalendarPeriod(anchor: Date, scale: TimeScale, direction: 1 | -1): Date {
  switch (scale) {
    case "day":
    case "hour":  return addDays(anchor, direction);
    case "week":  return addWeeks(anchor, direction);
    default:      return addMonths(anchor, direction); // month / year
  }
}
