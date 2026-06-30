import { describe, expect, it } from "vitest";
import { formatCompactTime } from "./formatters";

// USER_LOCALE is pinned to en-US in the test setup (src/test/setup.ts). Dates are constructed in local
// time and Intl formats in local time, so these assertions are timezone-independent.
describe("formatCompactTime (en-US)", () => {
  const at = (h: number, m = 0) => new Date(2026, 3, 17, h, m);

  it("formats whole hours compactly with a lowercase meridiem and no minutes", () => {
    expect(formatCompactTime(at(0), false)).toBe("12am");
    expect(formatCompactTime(at(1), false)).toBe("1am");
    expect(formatCompactTime(at(12), false)).toBe("12pm");
    expect(formatCompactTime(at(13), false)).toBe("1pm");
  });

  it("includes minutes when requested", () => {
    expect(formatCompactTime(at(13, 15), true)).toBe("1:15pm");
    expect(formatCompactTime(at(9, 5), true)).toBe("9:05am");
  });
});
