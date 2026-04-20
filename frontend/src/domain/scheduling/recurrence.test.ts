import { describe, it, expect } from "vitest";
import { expandRecurrence, parseRRule } from "./recurrence";
import type { OffTimeDefinition } from "./types";
import { utc, TZ } from "./test-helpers";

function makeDef(overrides: Partial<OffTimeDefinition> = {}): OffTimeDefinition {
  return {
    id: "off-1",
    siteId: "site-1",
    title: "Maintenance",
    type: "maintenance",
    appliesToAllSpaces: true,
    spaceIds: [],
    startMs: utc("2026-03-02T08:00:00Z"), // Monday
    endMs: utc("2026-03-02T10:00:00Z"),
    isRecurring: false,
    recurrenceRule: null,
    enabled: true,
    ...overrides,
  };
}

describe("parseRRule", () => {
  it("parses basic FREQ", () => {
    const r = parseRRule("FREQ=DAILY");
    expect(r.freq).toBe("DAILY");
    expect(r.interval).toBe(1);
  });

  it("parses INTERVAL", () => {
    const r = parseRRule("FREQ=WEEKLY;INTERVAL=2");
    expect(r.interval).toBe(2);
  });

  it("parses BYDAY", () => {
    const r = parseRRule("FREQ=WEEKLY;BYDAY=MO,WE,FR");
    expect(r.byDay).toEqual([1, 3, 5]);
  });

  it("parses UNTIL", () => {
    const r = parseRRule("FREQ=DAILY;UNTIL=20260401");
    expect(r.until).toEqual(new Date(Date.UTC(2026, 3, 1, 23, 59, 59, 999)));
  });

  it("parses COUNT", () => {
    const r = parseRRule("FREQ=DAILY;COUNT=5");
    expect(r.count).toBe(5);
  });

  it("throws on unsupported FREQ", () => {
    expect(() => parseRRule("FREQ=SECONDLY")).toThrow("Unsupported RRULE frequency");
  });

  it("throws on unsupported clause", () => {
    expect(() => parseRRule("FREQ=DAILY;BYMONTHDAY=15")).toThrow("Unsupported RRULE clause");
  });
});

describe("expandRecurrence", () => {
  const JAN = utc("2026-01-01T00:00:00Z");
  const DEC = utc("2026-12-31T23:59:59Z");

  it("returns non-recurring within window", () => {
    const def = makeDef();
    const ranges = expandRecurrence(def, JAN, DEC, TZ);
    expect(ranges).toHaveLength(1);
    expect(ranges[0].startMs).toBe(def.startMs);
    expect(ranges[0].endMs).toBe(def.endMs);
    expect(ranges[0].spaceIds).toBeNull(); // appliesToAllSpaces
  });

  it("returns empty for non-recurring outside window", () => {
    const def = makeDef({ startMs: utc("2025-06-01T08:00:00Z"), endMs: utc("2025-06-01T10:00:00Z") });
    expect(expandRecurrence(def, JAN, DEC, TZ)).toHaveLength(0);
  });

  it("preserves space scoping", () => {
    const def = makeDef({ appliesToAllSpaces: false, spaceIds: ["s1", "s2"] });
    const ranges = expandRecurrence(def, JAN, DEC, TZ);
    expect(ranges[0].spaceIds).toEqual(["s1", "s2"]);
  });

  it("expands DAILY recurrence", () => {
    const def = makeDef({
      isRecurring: true,
      recurrenceRule: "FREQ=DAILY;COUNT=3",
    });
    const ranges = expandRecurrence(def, JAN, DEC, TZ);
    expect(ranges).toHaveLength(3);
    // Each occurrence should be 2 hours apart by 1 day
    const durationMs = def.endMs - def.startMs;
    expect(ranges[1].endMs - ranges[1].startMs).toBe(durationMs);
    expect(ranges[2].endMs - ranges[2].startMs).toBe(durationMs);
  });

  it("expands WEEKLY with BYDAY", () => {
    // Start on Monday 2026-03-02, recur MO and WE, COUNT=4
    const def = makeDef({
      isRecurring: true,
      recurrenceRule: "FREQ=WEEKLY;BYDAY=MO,WE;COUNT=4",
    });
    const ranges = expandRecurrence(def, JAN, DEC, TZ);
    expect(ranges).toHaveLength(4);
  });

  it("respects UNTIL clause", () => {
    const def = makeDef({
      isRecurring: true,
      recurrenceRule: "FREQ=DAILY;UNTIL=20260304", // Mon, Tue, Wed
    });
    const ranges = expandRecurrence(def, JAN, DEC, TZ);
    expect(ranges).toHaveLength(3);
  });

  it("respects INTERVAL", () => {
    const def = makeDef({
      isRecurring: true,
      recurrenceRule: "FREQ=DAILY;INTERVAL=2;COUNT=3",
    });
    const ranges = expandRecurrence(def, JAN, DEC, TZ);
    expect(ranges).toHaveLength(3);
    // Day gaps: Mon, Wed, Fri
    const day = 86_400_000;
    expect(ranges[1].startMs - ranges[0].startMs).toBeCloseTo(2 * day, -4);
    expect(ranges[2].startMs - ranges[1].startMs).toBeCloseTo(2 * day, -4);
  });

  it("filters to window only", () => {
    const windowStart = utc("2026-03-03T00:00:00Z"); // Tuesday
    const windowEnd = utc("2026-03-05T00:00:00Z"); // Thursday
    const def = makeDef({
      isRecurring: true,
      recurrenceRule: "FREQ=DAILY;COUNT=7",
    });
    const ranges = expandRecurrence(def, windowStart, windowEnd, TZ);
    // Only Tue and Wed should be within window
    expect(ranges).toHaveLength(2);
  });

  it("handles MONTHLY recurrence", () => {
    const def = makeDef({
      startMs: utc("2026-01-15T08:00:00Z"),
      endMs: utc("2026-01-15T10:00:00Z"),
      isRecurring: true,
      recurrenceRule: "FREQ=MONTHLY;COUNT=3",
    });
    const ranges = expandRecurrence(def, JAN, DEC, TZ);
    expect(ranges).toHaveLength(3);
  });

  it("handles YEARLY recurrence", () => {
    const def = makeDef({
      startMs: utc("2024-06-15T08:00:00Z"),
      endMs: utc("2024-06-15T10:00:00Z"),
      isRecurring: true,
      recurrenceRule: "FREQ=YEARLY;COUNT=5",
    });
    // Window is 2026, so only 2026, 2027, 2028 would be in window if DEC was far enough,
    // but our window is just 2026, so only the 2026 occurrence
    const ranges = expandRecurrence(def, JAN, DEC, TZ);
    expect(ranges).toHaveLength(1);
  });

  it("handles DST spring-forward (Europe/Berlin March 29 2026)", () => {
    // CET -> CEST: clocks jump from 02:00 to 03:00 on 2026-03-29
    const def = makeDef({
      // 09:00 local = 08:00 UTC (CET = UTC+1)
      startMs: utc("2026-03-27T08:00:00Z"),
      endMs: utc("2026-03-27T10:00:00Z"),
      isRecurring: true,
      recurrenceRule: "FREQ=DAILY;COUNT=5",
    });
    const ranges = expandRecurrence(def, JAN, DEC, TZ);
    expect(ranges).toHaveLength(5);
    // Duration should be consistent (2h each)
    for (const r of ranges) {
      expect(r.endMs - r.startMs).toBe(2 * 3_600_000);
    }
  });
});
