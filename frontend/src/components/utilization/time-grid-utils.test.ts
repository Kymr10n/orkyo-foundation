import { describe, expect, it } from "vitest";
import {
  generateTimeColumns,
  getFetchWindow,
  overlapsOffTimeRange,
  parseTimeToHour,
  resolveColumnStartMs,
  utilizationGranularityForScale,
} from "./time-grid-utils";
import type { OffTimeRange } from "@foundation/src/domain/scheduling/types";

describe("time-grid-utils", () => {
  it("generates aligned column labels and counts for each scale", () => {
    const anchor = new Date(2026, 4, 11, 10, 20, 0);

    const year = generateTimeColumns("year", anchor);
    expect(year).toHaveLength(12);
    expect(year[0].label).toBe("May '26");
    expect(year[8].label).toBe("Jan '27");

    const month = generateTimeColumns("month", anchor);
    expect(month).toHaveLength(5);
    expect(month.map((c) => c.label)).toEqual(["May 11", "May 18", "May 25", "Jun 01", "Jun 08"]);

    const week = generateTimeColumns("week", anchor);
    expect(week).toHaveLength(7);
    expect(week[0].label).toBe("Mon 11");

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
});
