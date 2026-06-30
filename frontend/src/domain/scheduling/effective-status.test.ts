import { describe, expect, it } from "vitest";
import type { Request } from "@foundation/src/types/requests";
import { effectiveRequestStatus, withEffectiveStatus } from "./effective-status";

const NOW = Date.parse("2026-06-30T12:00:00Z");
const iso = (offsetMin: number) => new Date(NOW + offsetMin * 60_000).toISOString();

describe("effectiveRequestStatus", () => {
  it("passes manual states (cancelled/deferred) through, even mid-window", () => {
    expect(effectiveRequestStatus("cancelled", iso(-60), iso(60), NOW)).toBe("cancelled");
    expect(effectiveRequestStatus("deferred", iso(-60), iso(60), NOW)).toBe("deferred");
  });

  it("treats unscheduled work as new regardless of stored value", () => {
    expect(effectiveRequestStatus("in_progress", null, null, NOW)).toBe("new");
    expect(effectiveRequestStatus("done", iso(-60), undefined, NOW)).toBe("new");
  });

  it("derives the active lifecycle from the window vs now", () => {
    expect(effectiveRequestStatus("done", iso(120), iso(180), NOW)).toBe("new"); // future
    expect(effectiveRequestStatus("new", iso(-30), iso(30), NOW)).toBe("in_progress"); // spanning now
    expect(effectiveRequestStatus("new", iso(-120), iso(-60), NOW)).toBe("done"); // past
  });

  it("uses inclusive start and exclusive end", () => {
    expect(effectiveRequestStatus("new", iso(0), iso(60), NOW)).toBe("in_progress"); // now == start
    expect(effectiveRequestStatus("new", iso(-60), iso(0), NOW)).toBe("done"); // now == end
  });
});

describe("withEffectiveStatus", () => {
  const req = (id: string, status: Request["status"], start: string | null, end: string | null): Request =>
    ({ id, status, startTs: start, endTs: end }) as Request;

  it("returns the SAME array reference when no status flips", () => {
    const list = [req("a", "new", iso(120), iso(180))]; // future stays new
    expect(withEffectiveStatus(list, NOW)).toBe(list);
  });

  it("returns a new array with derived statuses when something flips", () => {
    const list = [
      req("a", "new", iso(-30), iso(30)), // → in_progress
      req("b", "new", iso(120), iso(180)), // stays new
    ];
    const out = withEffectiveStatus(list, NOW);
    expect(out).not.toBe(list);
    expect(out[0].status).toBe("in_progress");
    expect(out[1]).toBe(list[1]); // unchanged item keeps its reference
  });
});
