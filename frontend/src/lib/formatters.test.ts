import { describe, expect, it } from "vitest";
import { formatCompactTime } from "./formatters";

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
