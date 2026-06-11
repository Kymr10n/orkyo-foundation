import type { Conflict, Request, RequestStatus } from "@foundation/src/types/requests";
import { getStatusColor } from "@foundation/src/lib/utils/utils";

/**
 * Calendar event projection of a Request — see
 * `requirements/calendar-view-for-requests.md`.
 *
 * Kept framework-agnostic (no FullCalendar import) so the mapping and colour
 * rules are unit-testable in isolation. The shape is structurally compatible
 * with FullCalendar's `EventInput`, so `RequestCalendar` can pass these straight
 * through.
 */
export type ConflictSeverity = "error" | "warning" | null;

export interface CalendarEvent {
  id: string;
  title: string;
  start: string;
  end: string;
  /** Status base colour (getStatusColor) + conflict-severity overlay. */
  classNames: string[];
  editable: boolean;
  extendedProps: {
    requestId: string;
    status: RequestStatus;
    conflictSeverity: ConflictSeverity;
  };
}

/**
 * Reduce a request's conflicts to a single event severity: `error` dominates
 * `warning`; no conflicts → `null`. Mirrors the badge logic used elsewhere.
 */
export function getEventConflictSeverity(
  requestId: string,
  conflicts: Map<string, Conflict[]>,
): ConflictSeverity {
  const list = conflicts.get(requestId);
  if (!list || list.length === 0) return null;
  return list.some((c) => c.severity === "error") ? "error" : "warning";
}

/**
 * Event colour = request-status base (`getStatusColor`) plus a conflict overlay
 * ring (red = error, amber = warning). No new colour tokens are introduced; the
 * status classes are the same ones the status badges use.
 */
export function getEventClassNames(
  status: RequestStatus,
  severity: ConflictSeverity,
): string[] {
  const classes = ["orkyo-cal-event", ...getStatusColor(status).split(/\s+/).filter(Boolean)];
  if (severity === "error") classes.push("ring-2", "ring-red-500");
  else if (severity === "warning") classes.push("ring-2", "ring-amber-500");
  return classes;
}

/**
 * Map one scheduled request to a calendar event. Returns `null` for unscheduled
 * requests (no start/end) — the calendar only shows scheduled work.
 */
export function mapRequestToCalendarEvent(
  request: Request,
  conflicts: Map<string, Conflict[]>,
  editable: boolean,
): CalendarEvent | null {
  if (!request.startTs || !request.endTs) return null;
  const severity = getEventConflictSeverity(request.id, conflicts);
  return {
    id: request.id,
    title: request.name,
    start: request.startTs,
    end: request.endTs,
    classNames: getEventClassNames(request.status, severity),
    // Cancelled requests are shown for context but not draggable/resizable.
    editable: editable && request.status !== "cancelled",
    extendedProps: {
      requestId: request.id,
      status: request.status,
      conflictSeverity: severity,
    },
  };
}

/** Map a list of requests to calendar events, dropping the unscheduled ones. */
export function requestsToCalendarEvents(
  requests: Request[],
  conflicts: Map<string, Conflict[]>,
  editable: boolean,
): CalendarEvent[] {
  return requests
    .map((r) => mapRequestToCalendarEvent(r, conflicts, editable))
    .filter((e): e is CalendarEvent => e !== null);
}

/** Calendar view <-> shared TimeScale mapping (keeps the store window aligned). */
export type CalendarView = "timeGridDay" | "timeGridWeek" | "dayGridMonth";

export function scaleToCalendarView(scale: string): CalendarView {
  switch (scale) {
    case "day":
    case "hour":
      return "timeGridDay";
    case "week":
      return "timeGridWeek";
    default:
      return "dayGridMonth"; // month / year → month overview
  }
}

export function calendarViewToScale(view: string): "day" | "week" | "month" {
  switch (view) {
    case "timeGridDay":
      return "day";
    case "timeGridWeek":
      return "week";
    default:
      return "month";
  }
}
