import { describe, it, expect } from "vitest";
import {
  getEventConflictSeverity,
  getEventClassNames,
  mapRequestToCalendarEvent,
  requestsToCalendarEvents,
  scaleToCalendarView,
  calendarViewToScale,
} from "./request-calendar-events";
import { getStatusColor } from "@foundation/src/lib/utils/utils";
import { makeRequest, makeScheduledRequest } from "@foundation/src/test-utils/request-fixtures";
import type { Conflict } from "@foundation/src/types/requests";

const noConflicts = new Map<string, Conflict[]>();

function conflict(severity: "warning" | "error"): Conflict {
  return { id: `c-${severity}`, kind: "overlap", severity, message: "x" };
}

describe("getEventConflictSeverity", () => {
  it("returns null when the request has no conflicts", () => {
    expect(getEventConflictSeverity("r1", noConflicts)).toBeNull();
    expect(getEventConflictSeverity("r1", new Map([["r1", []]]))).toBeNull();
  });

  it("returns 'warning' when only warnings are present", () => {
    const map = new Map([["r1", [conflict("warning")]]]);
    expect(getEventConflictSeverity("r1", map)).toBe("warning");
  });

  it("lets error dominate warning", () => {
    const map = new Map([["r1", [conflict("warning"), conflict("error")]]]);
    expect(getEventConflictSeverity("r1", map)).toBe("error");
  });
});

describe("getEventClassNames", () => {
  it("uses the existing status colour as the base (no new tokens)", () => {
    const classes = getEventClassNames("planned", null);
    for (const token of getStatusColor("planned").split(/\s+/)) {
      expect(classes).toContain(token);
    }
    expect(classes).toContain("orkyo-cal-event");
    expect(classes.some((c) => c.startsWith("ring"))).toBe(false);
  });

  it("adds a red ring overlay for error severity", () => {
    const classes = getEventClassNames("done", "error");
    expect(classes).toContain("ring-2");
    expect(classes).toContain("ring-red-500");
  });

  it("adds an amber ring overlay for warning severity", () => {
    const classes = getEventClassNames("done", "warning");
    expect(classes).toContain("ring-amber-500");
  });
});

describe("mapRequestToCalendarEvent", () => {
  it("maps a scheduled request to an event using name/startTs/endTs", () => {
    const request = makeScheduledRequest("space-1", "2026-04-17T09:00:00Z", "2026-04-17T11:00:00Z", {
      name: "Install rig",
      status: "in_progress",
    });
    const event = mapRequestToCalendarEvent(request, noConflicts, true);
    expect(event).not.toBeNull();
    expect(event!.title).toBe("Install rig");
    expect(event!.start).toBe("2026-04-17T09:00:00Z");
    expect(event!.end).toBe("2026-04-17T11:00:00Z");
    expect(event!.extendedProps.requestId).toBe(request.id);
    expect(event!.extendedProps.status).toBe("in_progress");
    expect(event!.editable).toBe(true);
  });

  it("returns null for an unscheduled request (no start/end)", () => {
    expect(mapRequestToCalendarEvent(makeRequest(), noConflicts, true)).toBeNull();
  });

  it("is never editable when the page is read-only", () => {
    const request = makeScheduledRequest("s", "2026-04-17T09:00:00Z", "2026-04-17T11:00:00Z");
    expect(mapRequestToCalendarEvent(request, noConflicts, false)!.editable).toBe(false);
  });

  it("keeps cancelled requests visible but not editable", () => {
    const request = makeScheduledRequest("s", "2026-04-17T09:00:00Z", "2026-04-17T11:00:00Z", {
      status: "cancelled",
    });
    expect(mapRequestToCalendarEvent(request, noConflicts, true)!.editable).toBe(false);
  });
});

describe("requestsToCalendarEvents", () => {
  it("drops unscheduled requests and keeps scheduled ones", () => {
    const scheduled = makeScheduledRequest("s", "2026-04-17T09:00:00Z", "2026-04-17T11:00:00Z");
    const events = requestsToCalendarEvents([scheduled, makeRequest()], noConflicts, true);
    expect(events).toHaveLength(1);
    expect(events[0].id).toBe(scheduled.id);
  });
});

describe("scale <-> calendar view mapping", () => {
  it("maps store scales to calendar views", () => {
    expect(scaleToCalendarView("day")).toBe("timeGridDay");
    expect(scaleToCalendarView("hour")).toBe("timeGridDay");
    expect(scaleToCalendarView("week")).toBe("timeGridWeek");
    expect(scaleToCalendarView("month")).toBe("dayGridMonth");
    expect(scaleToCalendarView("year")).toBe("dayGridMonth");
  });

  it("maps calendar views back to scales", () => {
    expect(calendarViewToScale("timeGridDay")).toBe("day");
    expect(calendarViewToScale("timeGridWeek")).toBe("week");
    expect(calendarViewToScale("dayGridMonth")).toBe("month");
  });
});
