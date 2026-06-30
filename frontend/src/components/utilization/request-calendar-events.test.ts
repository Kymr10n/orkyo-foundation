import { describe, it, expect } from "vitest";
import {
  getEventConflictSeverity,
  getEventClassNames,
  mapRequestToCalendarEvent,
  requestsToCalendarEvents,
  scaleToCalendarView,
  calendarViewToScale,
} from "./request-calendar-events";
import { makeRequest, makeScheduledRequest } from "@foundation/src/test-utils/request-fixtures";
import { getStatusColor } from "@foundation/src/lib/utils";
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
  it("uses an opaque, self-contained status colour (planned → blue) with no ring when clean", () => {
    const classes = getEventClassNames("new", null);
    // Calendar events are opaque (unlike the translucent status badges): solid bg + border.
    // The `!` suffix is the Tailwind v4 important modifier — needed so the utilities beat
    // FullCalendar's unlayered injected `.fc-event` colour rules.
    expect(classes).toContain("orkyo-cal-event");
    expect(classes).toContain("bg-blue-100!");
    expect(classes).toContain("border-blue-200!");
    expect(classes.some((c) => c.startsWith("ring"))).toBe(false);
  });

  it("overrides the status colour with a red background for error severity", () => {
    const classes = getEventClassNames("done", "error");
    expect(classes).toContain("bg-red-100!");
    expect(classes).toContain("border-red-300!");
    expect(classes.some((c) => c.startsWith("ring"))).toBe(false);
    // Status colour must not leak through — overridden
    expect(classes.some((c) => c.startsWith("bg-emerald"))).toBe(false);
  });

  it("overrides the status colour with an amber background for warning severity", () => {
    const classes = getEventClassNames("done", "warning");
    expect(classes).toContain("bg-amber-100!");
    expect(classes).toContain("border-amber-300!");
    expect(classes.some((c) => c.startsWith("ring"))).toBe(false);
  });

  // Anti-drift: the calendar event colour and the list badge colour must agree on
  // hue per status (the badge is translucent, the event opaque — only the shade
  // differs). If someone re-introduces yellow/green on one side, this fails.
  it.each([
    ["new", "blue"],
    ["in_progress", "amber"],
    ["done", "emerald"],
  ] as const)("shares the %s colour family with getStatusColor (%s)", (status, family) => {
    expect(getEventClassNames(status, null).join(" ")).toContain(family);
    expect(getStatusColor(status)).toContain(family);
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
