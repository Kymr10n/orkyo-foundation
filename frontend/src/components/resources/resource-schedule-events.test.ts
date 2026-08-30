import { describe, it, expect } from "vitest";
import {
  absenceToCalendarEvent,
  assignmentToCalendarEvent,
  resourceScheduleEvents,
} from "./resource-schedule-events";
import type { ResourceAbsenceInfo } from "@foundation/src/lib/api/resource-absences-api";
import type { ResourceAssignmentInfo } from "@foundation/src/lib/api/resource-assignments-api";
import type { Conflict, Request } from "@foundation/src/types/requests";

const assignment = (overrides: Partial<ResourceAssignmentInfo> = {}): ResourceAssignmentInfo =>
  ({
    id: "a1",
    requestId: "r1",
    resourceId: "res1",
    resourceTypeKey: "machine",
    startUtc: "2026-06-01T09:00:00Z",
    endUtc: "2026-06-01T17:00:00Z",
    assignmentStatus: "Planned",
    createdAt: "2026-06-01T00:00:00Z",
    updatedAt: "2026-06-01T00:00:00Z",
    ...overrides,
  }) as ResourceAssignmentInfo;

const absence = (overrides: Partial<ResourceAbsenceInfo> = {}): ResourceAbsenceInfo =>
  ({
    id: "x1",
    resourceId: "res1",
    absenceType: "maintenance",
    title: "Annual service",
    startTs: "2026-06-02T08:00:00Z",
    endTs: "2026-06-02T12:00:00Z",
    isRecurring: false,
    enabled: true,
    createdAt: "2026-06-01T00:00:00Z",
    updatedAt: "2026-06-01T00:00:00Z",
    ...overrides,
  }) as ResourceAbsenceInfo;

const requests = new Map<string, Request>([["r1", { id: "r1", name: "Mill the bracket" } as Request]]);
const noConflicts = new Map<string, Conflict[]>();

describe("assignmentToCalendarEvent", () => {
  it("names the block after the work it books", () => {
    const event = assignmentToCalendarEvent(assignment(), requests, noConflicts, true);
    expect(event.title).toBe("Mill the bracket");
    expect(event.extendedProps.kind).toBe("assignment");
    expect(event.extendedProps.requestId).toBe("r1");
  });

  it("carries no request status, so a status filter cannot hide a booking", () => {
    expect(assignmentToCalendarEvent(assignment(), requests, noConflicts, true).extendedProps.status)
      .toBeUndefined();
  });

  it("wears the shared event base class, so the label reads as it does on the board", () => {
    // `orkyo-cal-event` owns the padding, radius and type scale. Colouring a block without it
    // is what made the text unreadable.
    expect(assignmentToCalendarEvent(assignment(), requests, noConflicts, true).classNames)
      .toContain("orkyo-cal-event");
  });

  it("takes the conflict colour of the request it books", () => {
    const conflicts = new Map<string, Conflict[]>([
      ["r1", [{ id: "c1", kind: "overlap", severity: "error", message: "x" } as Conflict]],
    ]);
    const event = assignmentToCalendarEvent(assignment(), requests, conflicts, true);

    expect(event.extendedProps.conflictSeverity).toBe("error");
    expect(event.classNames.some((c) => c.includes("red"))).toBe(true);
  });

  it("shows a warning-level conflict as a warning, not an error", () => {
    const conflicts = new Map<string, Conflict[]>([
      ["r1", [{ id: "c1", kind: "overlap", severity: "warning", message: "x" } as Conflict]],
    ]);
    expect(
      assignmentToCalendarEvent(assignment(), requests, conflicts, true).extendedProps
        .conflictSeverity,
    ).toBe("warning");
  });

  it("falls back to a label rather than showing a raw id", () => {
    const event = assignmentToCalendarEvent(assignment({ requestId: "gone" }), requests, noConflicts, true);
    expect(event.title).toBe("Booked");
    expect(event.title).not.toContain("gone");
  });
});

describe("absenceToCalendarEvent", () => {
  it("uses the reason as its label", () => {
    expect(absenceToCalendarEvent(absence(), true).title).toBe("Annual service");
  });

  it("falls back to the type when no reason was given", () => {
    expect(absenceToCalendarEvent(absence({ title: "" }), true).title).toBe("maintenance");
  });

  it("does not let a disabled absence be dragged", () => {
    // A disabled absence blocks nothing, so moving it would imply an effect it does not have.
    expect(absenceToCalendarEvent(absence({ enabled: false }), true).editable).toBe(false);
  });

  it("wears the shared event base class too", () => {
    expect(absenceToCalendarEvent(absence(), true).classNames).toContain("orkyo-cal-event");
  });

  it("is never editable for a viewer", () => {
    expect(absenceToCalendarEvent(absence(), false).editable).toBe(false);
  });
});

describe("resourceScheduleEvents", () => {
  it("returns both kinds, with bookings after absences so they stay clickable", () => {
    const events = resourceScheduleEvents([assignment()], [absence()], requests, noConflicts, true);
    expect(events.map((e) => e.extendedProps.kind)).toEqual(["absence", "assignment"]);
  });
});
