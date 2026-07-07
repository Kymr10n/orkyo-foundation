import { describe, expect, it } from "vitest";
import { formatCompactTime, formatDateDisplay, formatLocalized, formatTimeDisplay } from "./formatters";

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

describe("formatTimeDisplay", () => {
  it("returns an empty string for null/undefined/empty input", () => {
    expect(formatTimeDisplay(null)).toBe("");
    expect(formatTimeDisplay(undefined)).toBe("");
    expect(formatTimeDisplay("")).toBe("");
  });
  it("renders a 24h HH:mm time for a valid ISO string", () => {
    const local = new Date(2026, 3, 2, 13, 15);
    expect(formatTimeDisplay(local.toISOString())).toBe(formatCompactTime(local));
  });
});
