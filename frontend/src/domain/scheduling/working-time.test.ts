import { describe, it, expect } from "vitest";
import { isWorkingTime, nextWorkingStart, workingSegmentEnd } from "./working-time";
import { utc, makeSettings, makeCal } from "./test-helpers";

describe("isWorkingTime", () => {
  it("returns true during standard working hours on a weekday", () => {
    // Tuesday 2026-03-03 10:00 CET = 09:00 UTC
    const cal = makeCal();
    expect(isWorkingTime(cal, utc("2026-03-03T09:00:00Z"), null)).toBe(true);
  });

  it("returns false before working hours", () => {
    // Tuesday 06:00 CET = 05:00 UTC
    const cal = makeCal();
    expect(isWorkingTime(cal, utc("2026-03-03T05:00:00Z"), null)).toBe(false);
  });

  it("returns false after working hours", () => {
    // Tuesday 19:00 CET = 18:00 UTC
    const cal = makeCal();
    expect(isWorkingTime(cal, utc("2026-03-03T18:00:00Z"), null)).toBe(false);
  });

  it("returns false on Saturday", () => {
    // Saturday 2026-03-07 10:00 CET = 09:00 UTC
    const cal = makeCal();
    expect(isWorkingTime(cal, utc("2026-03-07T09:00:00Z"), null)).toBe(false);
  });

  it("returns false on Sunday", () => {
    const cal = makeCal();
    expect(isWorkingTime(cal, utc("2026-03-08T09:00:00Z"), null)).toBe(false);
  });

  it("returns true on weekend when weekendsEnabled is false", () => {
    const cal = makeCal({ settings: makeSettings({ weekendsEnabled: false }) });
    expect(isWorkingTime(cal, utc("2026-03-07T09:00:00Z"), null)).toBe(true);
  });

  it("returns false on a public holiday", () => {
    const cal = makeCal({
      settings: makeSettings({ publicHolidaysEnabled: true }),
      holidays: new Set(["2026-03-03"]),
    });
    expect(isWorkingTime(cal, utc("2026-03-03T09:00:00Z"), null)).toBe(false);
  });

  it("returns false during an off-time range", () => {
    const cal = makeCal({
      offTimeRanges: [
        {
          id: "off-1",
          startMs: utc("2026-03-03T09:00:00Z"),
          endMs: utc("2026-03-03T11:00:00Z"),
          title: "Maintenance",
          spaceIds: null,
        },
      ],
    });
    expect(isWorkingTime(cal, utc("2026-03-03T10:00:00Z"), null)).toBe(false);
  });

  it("returns true outside off-time range", () => {
    const cal = makeCal({
      offTimeRanges: [
        {
          id: "off-1",
          startMs: utc("2026-03-03T09:00:00Z"),
          endMs: utc("2026-03-03T11:00:00Z"),
          title: "Maintenance",
          spaceIds: null,
        },
      ],
    });
    expect(isWorkingTime(cal, utc("2026-03-03T12:00:00Z"), null)).toBe(true);
  });

  it("respects space-scoped off-time", () => {
    const cal = makeCal({
      offTimeRanges: [
        {
          id: "off-1",
          startMs: utc("2026-03-03T09:00:00Z"),
          endMs: utc("2026-03-03T11:00:00Z"),
          title: "Maintenance",
          spaceIds: ["space-A"],
        },
      ],
    });
    // Space A is blocked
    expect(isWorkingTime(cal, utc("2026-03-03T10:00:00Z"), "space-A")).toBe(false);
    // Space B is not blocked
    expect(isWorkingTime(cal, utc("2026-03-03T10:00:00Z"), "space-B")).toBe(true);
  });

  it("returns true when all settings are disabled", () => {
    const cal = makeCal({
      settings: makeSettings({
        workingHoursEnabled: false,
        weekendsEnabled: false,
        publicHolidaysEnabled: false,
      }),
    });
    // Saturday midnight
    expect(isWorkingTime(cal, utc("2026-03-07T01:00:00Z"), null)).toBe(true);
  });

  it("working day end is exclusive", () => {
    // Exactly at 18:00 CET = 17:00 UTC
    const cal = makeCal();
    expect(isWorkingTime(cal, utc("2026-03-03T17:00:00Z"), null)).toBe(false);
  });

  it("working day start is inclusive", () => {
    // Exactly at 08:00 CET = 07:00 UTC
    const cal = makeCal();
    expect(isWorkingTime(cal, utc("2026-03-03T07:00:00Z"), null)).toBe(true);
  });
});

describe("nextWorkingStart", () => {
  it("returns same instant if already in working time", () => {
    const cal = makeCal();
    const ts = utc("2026-03-03T09:00:00Z"); // Tue 10:00 CET
    expect(nextWorkingStart(cal, ts, null)).toBe(ts);
  });

  it("snaps to working day start when before hours", () => {
    const cal = makeCal();
    // Tuesday 06:00 CET = 05:00 UTC → should snap to 08:00 CET = 07:00 UTC
    const result = nextWorkingStart(cal, utc("2026-03-03T05:00:00Z"), null);
    expect(result).toBe(utc("2026-03-03T07:00:00Z"));
  });

  it("advances to next day when after hours", () => {
    const cal = makeCal();
    // Tuesday 19:00 CET → should snap to Wednesday 08:00 CET
    const result = nextWorkingStart(cal, utc("2026-03-03T18:00:00Z"), null);
    expect(result).toBe(utc("2026-03-04T07:00:00Z"));
  });

  it("skips weekend to Monday", () => {
    const cal = makeCal();
    // Saturday 2026-03-07 10:00 CET → Monday 2026-03-09 08:00 CET = 07:00 UTC
    const result = nextWorkingStart(cal, utc("2026-03-07T09:00:00Z"), null);
    expect(result).toBe(utc("2026-03-09T07:00:00Z"));
  });

  it("skips holiday", () => {
    const cal = makeCal({
      settings: makeSettings({ publicHolidaysEnabled: true }),
      holidays: new Set(["2026-03-03"]),
    });
    // Tuesday 2026-03-03 → should skip to Wednesday 2026-03-04 08:00 CET
    const result = nextWorkingStart(cal, utc("2026-03-03T09:00:00Z"), null);
    expect(result).toBe(utc("2026-03-04T07:00:00Z"));
  });

  it("skips past off-time", () => {
    const cal = makeCal({
      offTimeRanges: [
        {
          id: "off-1",
          startMs: utc("2026-03-03T09:00:00Z"),
          endMs: utc("2026-03-03T11:00:00Z"),
          title: "Maintenance",
          spaceIds: null,
        },
      ],
    });
    // In the middle of off-time → should skip to end of off-time (11:00 UTC = 12:00 CET)
    const result = nextWorkingStart(cal, utc("2026-03-03T10:00:00Z"), null);
    expect(result).toBe(utc("2026-03-03T11:00:00Z"));
  });

  it("throws when no working time available", () => {
    // All days are holidays
    const holidays = new Set<string>();
    for (let d = 0; d < 400; d++) {
      const date = new Date(2026, 0, 1 + d);
      holidays.add(date.toISOString().slice(0, 10));
    }
    const cal = makeCal({
      settings: makeSettings({ publicHolidaysEnabled: true }),
      holidays,
    });
    expect(() => nextWorkingStart(cal, utc("2026-01-01T09:00:00Z"), null)).toThrow(
      "No working time found"
    );
  });
});

describe("workingSegmentEnd", () => {
  it("returns working day end when no off-times", () => {
    const cal = makeCal();
    // Tuesday 10:00 CET = 09:00 UTC → segment ends at 18:00 CET = 17:00 UTC
    const result = workingSegmentEnd(cal, utc("2026-03-03T09:00:00Z"), null);
    expect(result).toBe(utc("2026-03-03T17:00:00Z"));
  });

  it("returns off-time start when off-time interrupts", () => {
    const cal = makeCal({
      offTimeRanges: [
        {
          id: "off-1",
          startMs: utc("2026-03-03T12:00:00Z"),
          endMs: utc("2026-03-03T14:00:00Z"),
          title: "Lunch",
          spaceIds: null,
        },
      ],
    });
    // Starting at 09:00 UTC (10:00 CET) → segment ends at 12:00 UTC (off-time start)
    const result = workingSegmentEnd(cal, utc("2026-03-03T09:00:00Z"), null);
    expect(result).toBe(utc("2026-03-03T12:00:00Z"));
  });

  it("ignores off-time for different space", () => {
    const cal = makeCal({
      offTimeRanges: [
        {
          id: "off-1",
          startMs: utc("2026-03-03T12:00:00Z"),
          endMs: utc("2026-03-03T14:00:00Z"),
          title: "Maintenance",
          spaceIds: ["space-A"],
        },
      ],
    });
    // Space B doesn't have this off-time
    const result = workingSegmentEnd(cal, utc("2026-03-03T09:00:00Z"), "space-B");
    expect(result).toBe(utc("2026-03-03T17:00:00Z"));
  });
});
