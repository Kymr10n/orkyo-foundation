import { describe, expect, it } from "vitest";
import {
  coversOffTimeRange,
  generateTimeColumns,
  getFetchWindow,
  isAnchorStale,
  overlapsOffTimeRange,
  parseTimeToHour,
  resolveColumnStartMs,
  utilizationGranularityForScale,
  viewPositionPercent,
} from "./time-grid-utils";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";

describe("time-grid-utils", () => {
  it("generates aligned column labels and counts for each scale", () => {
    const anchor = new Date(2026, 4, 11, 10, 20, 0);

    // Date labels are locale-aware (pinned en-US in the test setup); times default to 24h
    // (formatCompactTime → "10:00"/"10:15"), shared with the calendar.
    const year = generateTimeColumns("year", anchor);
    expect(year).toHaveLength(12);
    expect(year[0].label).toBe("May 26");
    expect(year[8].label).toBe("Jan 27");

    const month = generateTimeColumns("month", anchor);
    expect(month).toHaveLength(5);
    expect(month.map((c) => c.label)).toEqual(["May 11", "May 18", "May 25", "Jun 01", "Jun 08"]);

    const week = generateTimeColumns("week", anchor);
    expect(week).toHaveLength(7);
    // en-US Intl renders weekday+day as "11 Mon" — matches the calendar's day header ("29 Mon").
    expect(week[0].label).toBe("11 Mon");

    const day = generateTimeColumns("day", anchor);
    expect(day).toHaveLength(24);
    expect(day[0].label).toBe("10:00");

    const hour = generateTimeColumns("hour", anchor);
    expect(hour).toHaveLength(4);
    expect(hour.map((c) => c.label)).toEqual(["10:15", "10:30", "10:45", "11:00"]);
  });

  it("maps UI scales to API utilization granularities", () => {
    expect(utilizationGranularityForScale("year")).toBe("month");
    expect(utilizationGranularityForScale("month")).toBe("week");
    expect(utilizationGranularityForScale("week")).toBe("day");
    expect(utilizationGranularityForScale("day")).toBe("hour");
    expect(utilizationGranularityForScale("hour")).toBe("minute");
  });

  it("marks weekend and outside-working-hours columns when requested", () => {
    const weekendColumns = generateTimeColumns("week", new Date(2026, 4, 11), true);
    expect(weekendColumns.map((c) => c.isWeekend ?? false)).toEqual([
      false,
      false,
      false,
      false,
      false,
      true,
      true,
    ]);

    const dayColumns = generateTimeColumns("day", new Date(2026, 4, 11), false, {
      enabled: true,
      start: 8,
      end: 17,
    });
    expect(dayColumns[0].isOutsideWorkingHours).toBe(true);
    expect(dayColumns[8].isOutsideWorkingHours).toBe(false);
    expect(dayColumns[17].isOutsideWorkingHours).toBe(true);
  });

  it("parses hour strings and detects scoped off-time overlap", () => {
    expect(parseTimeToHour("08:30")).toBe(8);

    const offTimeRanges: OffTimeRange[] = [
      {
        id: "ot-all",
        title: "Site closed",
        startMs: Date.parse("2026-05-11T09:00:00Z"),
        endMs: Date.parse("2026-05-11T10:00:00Z"),
        resourceIds: null,
      },
      {
        id: "ot-space-2",
        title: "Space 2 maintenance",
        startMs: Date.parse("2026-05-11T12:00:00Z"),
        endMs: Date.parse("2026-05-11T13:00:00Z"),
        resourceIds: ["space-2"],
      },
    ];

    expect(
      overlapsOffTimeRange(
        "space-1",
        Date.parse("2026-05-11T09:30:00Z"),
        Date.parse("2026-05-11T10:30:00Z"),
        offTimeRanges,
      ),
    ).toBe(true);
    expect(
      overlapsOffTimeRange(
        "space-1",
        Date.parse("2026-05-11T12:15:00Z"),
        Date.parse("2026-05-11T12:45:00Z"),
        offTimeRanges,
      ),
    ).toBe(false);
    expect(
      overlapsOffTimeRange(
        "space-2",
        Date.parse("2026-05-11T12:15:00Z"),
        Date.parse("2026-05-11T12:45:00Z"),
        offTimeRanges,
      ),
    ).toBe(true);
  });

  it("tints a column as off-time only when a range covers it end to end", () => {
    // Two full-day weekend ranges (Sat 2026-05-16 and Sun 2026-05-17, UTC) —
    // exactly what generateWeekendRanges emits.
    const sat = Date.parse("2026-05-16T00:00:00Z");
    const sun = Date.parse("2026-05-17T00:00:00Z");
    const mon = Date.parse("2026-05-18T00:00:00Z");
    const ranges: OffTimeRange[] = [
      { id: "w-sat", title: "Weekend", startMs: sat, endMs: sun, resourceIds: null },
      { id: "w-sun", title: "Weekend", startMs: sun, endMs: mon, resourceIds: null },
    ];

    // Week view: a Saturday day-column is fully covered → tinted.
    expect(coversOffTimeRange("space-1", sat, sun, ranges)).toBe(true);

    // Month view: a Mon→Mon week column merely overlaps the weekend → NOT tinted.
    const weekStart = Date.parse("2026-05-11T00:00:00Z");
    const weekEnd = Date.parse("2026-05-18T00:00:00Z");
    expect(coversOffTimeRange("space-1", weekStart, weekEnd, ranges)).toBe(false);
    // The previous overlap-based logic is what wrongly painted the whole column:
    expect(overlapsOffTimeRange("space-1", weekStart, weekEnd, ranges)).toBe(true);

    // Year view: a whole-month column is likewise not covered by a 1-day range.
    const monthStart = Date.parse("2026-05-01T00:00:00Z");
    const monthEnd = Date.parse("2026-06-01T00:00:00Z");
    expect(coversOffTimeRange("space-1", monthStart, monthEnd, ranges)).toBe(false);
  });

  it("scopes full-coverage off-time to the matching resource", () => {
    const start = Date.parse("2026-05-16T00:00:00Z");
    const end = Date.parse("2026-05-17T00:00:00Z");
    const ranges: OffTimeRange[] = [
      { id: "m", title: "Maintenance", startMs: start, endMs: end, resourceIds: ["space-2"] },
    ];
    expect(coversOffTimeRange("space-2", start, end, ranges)).toBe(true);
    expect(coversOffTimeRange("space-1", start, end, ranges)).toBe(false);
  });

  it("resolves the dropped column from pointer x within the track rect", () => {
    // Three equal 100px columns spanning a 300px-wide track starting at x=200.
    const starts = [1000, 2000, 3000];
    const left = 200;
    const width = 300;

    expect(resolveColumnStartMs(250, left, width, starts)).toBe(1000); // first column
    expect(resolveColumnStartMs(350, left, width, starts)).toBe(2000); // middle column
    expect(resolveColumnStartMs(550, left, width, starts)).toBe(3000); // last column
    // Pointer left of / past the track clamps to the first / last column.
    expect(resolveColumnStartMs(0, left, width, starts)).toBe(1000);
    expect(resolveColumnStartMs(9999, left, width, starts)).toBe(3000);
  });

  it("falls back to the first column start for a degenerate track", () => {
    expect(resolveColumnStartMs(50, 0, 0, [1000, 2000])).toBe(1000);
    expect(resolveColumnStartMs(50, 0, 100, [])).toBe(0);
  });

  it("buffers the fetch window per scale and snaps to the unit start", () => {
    const anchor = new Date(2026, 4, 13, 10, 0, 0); // Wed 13 May 2026

    const week = getFetchWindow("week", anchor);
    // Snapped to Monday and buffered ±4 weeks → spans 9 weeks, covers the anchor.
    expect(week.from.getTime()).toBeLessThan(anchor.getTime());
    expect(week.to.getTime()).toBeGreaterThan(anchor.getTime());
    expect(Math.round((week.to.getTime() - week.from.getTime()) / (7 * 86400_000))).toBe(9);

    // Navigating within the same week keeps an identical (snapped) window → stable query key.
    const sameWeek = getFetchWindow("week", new Date(2026, 4, 15, 18, 0, 0)); // Fri same week
    expect(sameWeek.from.getTime()).toBe(week.from.getTime());
    expect(sameWeek.to.getTime()).toBe(week.to.getTime());

    // Month window is wider than week; year wider than month.
    const month = getFetchWindow("month", anchor);
    const year = getFetchWindow("year", anchor);
    const span = (w: { from: Date; to: Date }) => w.to.getTime() - w.from.getTime();
    expect(span(month)).toBeGreaterThan(span(week));
    expect(span(year)).toBeGreaterThan(span(month));
  });

  describe("isAnchorStale", () => {
    // Local-time constructors (month is 0-indexed → 6 = July) so `startOfDay` (local) is timezone-stable.
    const now = new Date(2026, 6, 5, 9, 30);
    it("is true when the anchor's day is before today", () => {
      expect(isAnchorStale(new Date(2026, 6, 4, 23, 59), now)).toBe(true);
      expect(isAnchorStale(new Date(2026, 5, 29), now)).toBe(true);
    });
    it("is false earlier the same day (only the calendar day matters)", () => {
      expect(isAnchorStale(new Date(2026, 6, 5, 0, 0), now)).toBe(false);
      expect(isAnchorStale(new Date(2026, 6, 5, 9, 29), now)).toBe(false);
    });
    it("is false for a future anchor", () => {
      expect(isAnchorStale(new Date(2026, 6, 6), now)).toBe(false);
    });
  });

  describe("viewPositionPercent", () => {
    it("returns the percentage for an instant inside the range", () => {
      expect(viewPositionPercent(25, 0, 100)).toBe(25);
      expect(viewPositionPercent(50, 0, 100)).toBe(50);
    });

    it("returns 0 at the (inclusive) start", () => {
      expect(viewPositionPercent(0, 0, 100)).toBe(0);
    });

    it("returns null before the start and at/after the (exclusive) end", () => {
      expect(viewPositionPercent(-1, 0, 100)).toBeNull();
      expect(viewPositionPercent(100, 0, 100)).toBeNull(); // end is exclusive
      expect(viewPositionPercent(150, 0, 100)).toBeNull();
    });

    it("returns null for a non-positive span", () => {
      expect(viewPositionPercent(0, 100, 100)).toBeNull();
      expect(viewPositionPercent(0, 100, 50)).toBeNull();
    });
  });
});
