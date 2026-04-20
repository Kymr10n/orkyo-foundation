import { describe, it, expect } from "vitest";
import { generateWeekendRanges } from "./weekend-ranges";

describe("generateWeekendRanges", () => {
  // Mon 2026-04-06 to Sun 2026-04-12 — contains Sat 11 + Sun 12
  const MON = new Date("2026-04-06T00:00:00Z").getTime();
  const SUN_END = new Date("2026-04-13T00:00:00Z").getTime();

  it("generates ranges for Saturday and Sunday within the window", () => {
    const ranges = generateWeekendRanges(MON, SUN_END);
    const titles = ranges.map((r) => r.title);
    expect(titles.every((t) => t === "Weekend")).toBe(true);
    expect(ranges).toHaveLength(2); // Sat + Sun
  });

  it("each range spans a full day", () => {
    const ranges = generateWeekendRanges(MON, SUN_END);
    for (const r of ranges) {
      expect(r.endMs - r.startMs).toBe(24 * 60 * 60 * 1000);
    }
  });

  it("applies to all spaces (spaceIds is null)", () => {
    const ranges = generateWeekendRanges(MON, SUN_END);
    for (const r of ranges) {
      expect(r.spaceIds).toBeNull();
    }
  });

  it("returns empty array for a weekday-only window", () => {
    // Mon–Fri
    const fri = new Date("2026-04-10T00:00:00Z").getTime();
    expect(generateWeekendRanges(MON, fri)).toHaveLength(0);
  });

  it("clamps ranges to the window boundaries", () => {
    // Window starts mid-Saturday
    const satNoon = new Date("2026-04-11T12:00:00Z").getTime();
    const ranges = generateWeekendRanges(satNoon, SUN_END);
    expect(ranges[0].startMs).toBe(satNoon);
  });
});
