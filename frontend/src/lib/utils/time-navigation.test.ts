import { describe, it, expect } from "vitest";
import { addDays, addWeeks, addMonths, differenceInCalendarDays } from "date-fns";
import { navigateCalendarPeriod } from "./time-navigation";

const anchor = new Date("2026-03-15T00:00:00");

describe("navigateCalendarPeriod (full-period paging)", () => {
  it("pages a week scale by a whole week", () => {
    expect(navigateCalendarPeriod(anchor, "week", 1)).toEqual(addWeeks(anchor, 1));
    expect(differenceInCalendarDays(navigateCalendarPeriod(anchor, "week", 1), anchor)).toBe(7);
  });

  it("pages a month scale by a whole month", () => {
    expect(navigateCalendarPeriod(anchor, "month", 1)).toEqual(addMonths(anchor, 1));
  });

  it("collapses the extremes to the calendar's real views (hour→day, year→month)", () => {
    expect(navigateCalendarPeriod(anchor, "hour", 1)).toEqual(addDays(anchor, 1));
    expect(navigateCalendarPeriod(anchor, "day", 1)).toEqual(addDays(anchor, 1));
    expect(navigateCalendarPeriod(anchor, "year", 1)).toEqual(addMonths(anchor, 1));
  });
});
