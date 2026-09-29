import { describe, expect, it } from "vitest";
import {
  toDateTimeLocalValue,
  formatCompactTime,
  formatDateDisplay,
  formatDateTimeDisplay,
  formatDateTimeShort,
  formatLocalized,
  formatPeriod,
  formatScheduledWindow,
} from "./formatters";

// Dates are constructed in local time and Intl formats in local time, so these are TZ-independent.
describe("formatCompactTime (24h default)", () => {
  const at = (h: number, m = 0) => new Date(2026, 3, 17, h, m);

  it("formats the time of day as 24h HH:mm", () => {
    expect(formatCompactTime(at(0))).toBe("00:00");
    expect(formatCompactTime(at(1))).toBe("01:00");
    expect(formatCompactTime(at(13))).toBe("13:00");
    expect(formatCompactTime(at(13, 15))).toBe("13:15");
    expect(formatCompactTime(at(9, 5))).toBe("09:05");
    expect(formatCompactTime(at(23))).toBe("23:00");
  });
});

describe("formatDateDisplay", () => {
  it("returns a dash for null/undefined/empty input", () => {
    expect(formatDateDisplay(null)).toBe("-");
    expect(formatDateDisplay(undefined)).toBe("-");
    expect(formatDateDisplay("")).toBe("-");
  });
  it("renders a locale-aware medium date for a valid ISO string", () => {
    const iso = "2026-04-02T10:30:00Z";
    expect(formatDateDisplay(iso)).toBe(formatLocalized(new Date(iso), { dateStyle: "medium" }));
  });
  it("takes a Date and a caller's empty text", () => {
    const d = new Date(2026, 3, 2);
    expect(formatDateDisplay(d)).toBe(formatLocalized(d, { dateStyle: "medium" }));
    expect(formatDateDisplay(null, "—")).toBe("—");
  });
});

describe("formatDateTimeDisplay", () => {
  it("renders date, year and 24h time in the user's locale", () => {
    const d = new Date(2026, 3, 2, 14, 30);
    const text = formatDateTimeDisplay(d);
    expect(text).toContain("2026");
    expect(text).toContain("14:30");
    expect(formatDateTimeDisplay(d.toISOString())).toBe(text);
  });
});

describe("formatScheduledWindow", () => {
  it("says so when either end is missing, because half a window is not a schedule", () => {
    expect(formatScheduledWindow(null, "2026-04-07T00:00:00")).toBe("Unscheduled");
    expect(formatScheduledWindow("2026-04-02T00:00:00", null)).toBe("Unscheduled");
    expect(formatScheduledWindow(undefined, undefined)).toBe("Unscheduled");
  });

  it("renders the window and its length", () => {
    const start = "2026-04-02T08:00:00";
    const end = "2026-04-07T17:00:00";
    const opts = { month: "short", day: "numeric" } as const;
    expect(formatScheduledWindow(start, end)).toBe(
      `${formatLocalized(new Date(start), opts)} – ${formatLocalized(new Date(end), opts)} · 6d`,
    );
  });

  it("counts a task that starts and finishes on one day as one day, not zero", () => {
    expect(formatScheduledWindow("2026-04-02T08:00:00", "2026-04-02T17:00:00")).toContain("· 1d");
  });

  it("spans a month boundary", () => {
    expect(formatScheduledWindow("2026-04-29T09:00:00", "2026-05-02T09:00:00")).toContain("· 4d");
  });
});

describe("formatPeriod", () => {
  const start = "2026-04-17T09:05:00Z";
  const end = "2026-04-17T13:15:00Z";

  it("returns an empty string when either end is missing", () => {
    expect(formatPeriod("", end)).toBe("");
    expect(formatPeriod(start, "")).toBe("");
  });

  it("joins the two short stamps with an en dash", () => {
    expect(formatPeriod(start, end)).toBe(`${formatDateTimeShort(start)} – ${formatDateTimeShort(end)}`);
    expect(formatDateTimeShort(start)).toMatch(/\d/);
  });
});

describe("toDateTimeLocalValue", () => {
  it("renders local date and time for a datetime-local input", () => {
    expect(toDateTimeLocalValue(new Date(2026, 0, 5, 9, 7))).toBe("2026-01-05T09:07");
  });

  it("accepts an ISO string and answers empty for an unparseable one", () => {
    const iso = new Date(2026, 5, 30, 23, 59).toISOString();
    expect(toDateTimeLocalValue(iso)).toBe("2026-06-30T23:59");
    expect(toDateTimeLocalValue("not a date")).toBe("");
  });
});
