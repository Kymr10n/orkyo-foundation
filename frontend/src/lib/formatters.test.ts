import { describe, expect, it } from "vitest";
import {
  formatCompactTime,
  formatDateDisplay,
  formatLocalized,
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
