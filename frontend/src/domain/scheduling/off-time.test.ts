import { describe, expect, it } from "vitest";
import { coversOffTimeRange } from "./off-time";
import { makeOffTimeRange } from "./test-helpers";

describe("off-time predicate", () => {
  it("honours per-resource scoping", () => {
    const offTimeRanges = [
      makeOffTimeRange("2026-05-11T09:00:00Z", "2026-05-11T10:00:00Z"),
      makeOffTimeRange("2026-05-11T12:00:00Z", "2026-05-11T13:00:00Z", ["space-2"]),
    ];

    // A site-wide range covers any resource's column inside it.
    expect(
      coversOffTimeRange(
        "space-1",
        Date.parse("2026-05-11T09:15:00Z"),
        Date.parse("2026-05-11T09:45:00Z"),
        offTimeRanges,
      ),
    ).toBe(true);
    // A range scoped to space-2 says nothing about space-1.
    expect(
      coversOffTimeRange(
        "space-1",
        Date.parse("2026-05-11T12:15:00Z"),
        Date.parse("2026-05-11T12:45:00Z"),
        offTimeRanges,
      ),
    ).toBe(false);
    expect(
      coversOffTimeRange(
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
    const ranges = [
      makeOffTimeRange("2026-05-16T00:00:00Z", "2026-05-17T00:00:00Z"),
      makeOffTimeRange("2026-05-17T00:00:00Z", "2026-05-18T00:00:00Z"),
    ];

    // Week view: a Saturday day-column is fully covered → tinted.
    expect(coversOffTimeRange("space-1", sat, sun, ranges)).toBe(true);

    // Month view: a Mon→Mon week column merely overlaps the weekend → NOT tinted.
    const weekStart = Date.parse("2026-05-11T00:00:00Z");
    const weekEnd = Date.parse("2026-05-18T00:00:00Z");
    expect(coversOffTimeRange("space-1", weekStart, weekEnd, ranges)).toBe(false);

    // Year view: a whole-month column is likewise not covered by a 1-day range.
    const monthStart = Date.parse("2026-05-01T00:00:00Z");
    const monthEnd = Date.parse("2026-06-01T00:00:00Z");
    expect(coversOffTimeRange("space-1", monthStart, monthEnd, ranges)).toBe(false);
  });

  it("scopes full-coverage off-time to the matching resource", () => {
    const start = Date.parse("2026-05-16T00:00:00Z");
    const end = Date.parse("2026-05-17T00:00:00Z");
    const ranges = [makeOffTimeRange("2026-05-16T00:00:00Z", "2026-05-17T00:00:00Z", ["space-2"])];
    expect(coversOffTimeRange("space-2", start, end, ranges)).toBe(true);
    expect(coversOffTimeRange("space-1", start, end, ranges)).toBe(false);
  });
});
