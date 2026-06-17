import type { Conflict, Request, RequestStatus } from "@foundation/src/types/requests";

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
 * Opaque bg + matching border for calendar event blocks. Kept separate from
 * getStatusColor (which is badge-sized and intentionally transparent) so the
 * two call sites can diverge independently.
 *
 * The `!` important modifier is required: FullCalendar injects its `.fc-event`
 * colour rules *unlayered* at runtime, and under Tailwind v4 (utilities live in
 * `@layer utilities`) unlayered CSS always beats layered utilities — so without
 * `!important` request-calendar.css's `--fc-event-*: transparent` defaults win
 * and the blocks render with no fill/border/text colour.
 */
function getCalendarEventColor(status: RequestStatus): string {
  switch (status) {
    case "planned":
      return "bg-blue-100! dark:bg-blue-950! border-blue-200! dark:border-blue-800! text-blue-800! dark:text-blue-300!";
    case "in_progress":
      return "bg-amber-100! dark:bg-amber-950! border-amber-200! dark:border-amber-800! text-amber-800! dark:text-amber-300!";
    case "done":
      return "bg-emerald-100! dark:bg-emerald-950! border-emerald-200! dark:border-emerald-800! text-emerald-800! dark:text-emerald-300!";
    case "cancelled":
      return "bg-muted! border-muted-foreground/30! text-muted-foreground! line-through";
    default:
      return "bg-muted! border-muted-foreground/30! text-muted-foreground!";
  }
}

/**
 * Conflict-severity colours — the single source of truth shared by the calendar
 * event blocks (below) and the RequestCalendar legend swatches. Ring overlays are
 * not used — FullCalendar's nested overflow:hidden clips them, so severity is
 * expressed as a full background override. `!` important is needed for the same
 * reason as getCalendarEventColor (FC's unlayered rules beat layered utilities).
 */
export const SEVERITY_EVENT_CLASS: Record<"error" | "warning", string[]> = {
  error: ["bg-red-100!", "dark:bg-red-950!", "border-red-300!", "dark:border-red-800!",
    "text-red-900!", "dark:text-red-200!"],
  warning: ["bg-amber-100!", "dark:bg-amber-950!", "border-amber-300!", "dark:border-amber-800!",
    "text-amber-900!", "dark:text-amber-200!"],
};

/** Legend swatch (bg + border only) for the same severities. */
export const SEVERITY_SWATCH: Record<"error" | "warning", string> = {
  error: "bg-red-100 dark:bg-red-950 border-red-300 dark:border-red-800",
  warning: "bg-amber-100 dark:bg-amber-950 border-amber-300 dark:border-amber-800",
};

/** Event colour = opaque status block, overridden by conflict severity. */
export function getEventClassNames(
  status: RequestStatus,
  severity: ConflictSeverity,
): string[] {
  if (severity) {
    return ["orkyo-cal-event", ...SEVERITY_EVENT_CLASS[severity]];
  }
  return ["orkyo-cal-event", ...getCalendarEventColor(status).split(/\s+/).filter(Boolean)];
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
