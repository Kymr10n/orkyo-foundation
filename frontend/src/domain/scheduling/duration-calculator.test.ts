import { describe, it, expect } from "vitest";
import {
  computeWorkingDuration,
} from "./duration-calculator";
import { utc, makeCal, HOUR } from "./test-helpers";

describe("computeWorkingDuration", () => {
  it("full working day = 10h", () => {
    const cal = makeCal();
    // Monday 08:00 CET to 18:00 CET
    const duration = computeWorkingDuration(
      cal,
      utc("2026-03-02T07:00:00Z"),
      utc("2026-03-02T17:00:00Z"),
      null
    );
    expect(duration).toBe(10 * HOUR);
  });

  it("span with lunch off-time", () => {
    const cal = makeCal({
      offTimeRanges: [
        {
          id: "lunch",
          startMs: utc("2026-03-03T11:00:00Z"),
          endMs: utc("2026-03-03T12:00:00Z"),
          title: "Lunch",
          spaceIds: null,
        },
      ],
    });
    // 08:00-18:00 CET with 1h lunch = 9h working
    const duration = computeWorkingDuration(
      cal,
      utc("2026-03-03T07:00:00Z"),
      utc("2026-03-03T17:00:00Z"),
      null
    );
    expect(duration).toBe(9 * HOUR);
  });

  it("span including weekend = only weekday hours", () => {
    const cal = makeCal();
    // Friday 08:00 to Monday 18:00 CET
    const duration = computeWorkingDuration(
      cal,
      utc("2026-03-06T07:00:00Z"), // Fri 08:00 CET
      utc("2026-03-09T17:00:00Z"), // Mon 18:00 CET
      null
    );
    // Fri 10h + Mon 10h = 20h
    expect(duration).toBe(20 * HOUR);
  });

  it("zero for range entirely outside working time", () => {
    const cal = makeCal();
    // Saturday noon to Sunday noon
    const duration = computeWorkingDuration(
      cal,
      utc("2026-03-07T11:00:00Z"),
      utc("2026-03-08T11:00:00Z"),
      null
    );
    expect(duration).toBe(0);
  });

  it("empty range returns 0", () => {
    const cal = makeCal();
    expect(computeWorkingDuration(cal, utc("2026-03-03T09:00:00Z"), utc("2026-03-03T09:00:00Z"), null)).toBe(0);
  });

  it("reversed range returns 0", () => {
    const cal = makeCal();
    expect(computeWorkingDuration(cal, utc("2026-03-03T12:00:00Z"), utc("2026-03-03T09:00:00Z"), null)).toBe(0);
  });
});
