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

/**
 * What a block on the calendar stands for. The grid itself is indifferent — it reads only the id
 * and the conflict severity — but a host that mixes kinds needs to know which mutation a drag
 * belongs to, and a request-only host keeps behaving exactly as before.
 */
export type CalendarEventKind = "request" | "absence" | "assignment";

export interface CalendarEvent {
  id: string;
  title: string;
  start: string;
  end: string;
  /** Status base colour (getStatusColor) + conflict-severity overlay. */
  classNames: string[];
  editable: boolean;
  extendedProps: {
    kind: CalendarEventKind;
    /** Present on request and assignment events; absences belong to no request. */
    requestId?: string;
    /** Request status. Absences and assignments carry none. */
    status?: RequestStatus;
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
 * Translucent status fill + colored border for calendar event blocks. Mirrors
 * the utilization grids (ScheduledRequestOverlay / People segments), which read
 * the request *status* off a neutral `text-foreground` label sitting on a light
 * alpha tint — the house style (off-time tints at /15, the floorplan at /25) — so
 * the time grid stays visible behind the block instead of an opaque slab. Kept
 * separate from getStatusColor (badge-sized) so the two call sites can diverge.
 *
 * The `!` important modifier is required: FullCalendar injects its `.fc-event`
 * colour rules *unlayered* at runtime, and under Tailwind v4 (utilities live in
 * `@layer utilities`) unlayered CSS always beats layered utilities — so without
 * `!important` request-calendar.css's `--fc-event-*: transparent` defaults win
 * and the blocks render with no fill/border/text colour.
 */
function getCalendarEventColor(status: RequestStatus): string {
  switch (status) {
    case "new":
      return "bg-blue-500/15! dark:bg-blue-500/25! border-blue-500/40! text-foreground!";
    case "in_progress":
      return "bg-amber-500/15! dark:bg-amber-500/25! border-amber-500/40! text-foreground!";
    case "done":
      return "bg-emerald-500/15! dark:bg-emerald-500/25! border-emerald-500/40! text-foreground!";
    case "deferred":
      return "bg-slate-500/15! dark:bg-slate-500/25! border-slate-400/40! text-muted-foreground!";
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
  error: ["bg-red-500/15!", "dark:bg-red-500/25!", "border-red-500/60!", "text-foreground!"],
  warning: ["bg-amber-500/15!", "dark:bg-amber-500/25!", "border-amber-500/60!", "text-foreground!"],
};

/**
 * Legend swatch (bg + border only) per status — the same palette
 * {@link getCalendarEventColor} paints events with, minus the `!important` those need to beat
 * FullCalendar's unlayered rules. Exported so the legend and the status filter read from one
 * place instead of restating the colours.
 */
export const STATUS_SWATCH: Record<RequestStatus, string> = {
  new: "bg-blue-500/15 dark:bg-blue-500/25 border-blue-500/40",
  in_progress: "bg-amber-500/15 dark:bg-amber-500/25 border-amber-500/40",
  done: "bg-emerald-500/15 dark:bg-emerald-500/25 border-emerald-500/40",
  deferred: "bg-slate-500/15 dark:bg-slate-500/25 border-slate-400/40",
  cancelled: "bg-muted border-muted-foreground/30",
};

/** Legend swatch (bg + border only) for the same severities. */
export const SEVERITY_SWATCH: Record<"error" | "warning", string> = {
  error: "bg-red-500/15 dark:bg-red-500/25 border-red-500/60",
  warning: "bg-amber-500/15 dark:bg-amber-500/25 border-amber-500/60",
};

/** Event colour = translucent status block, overridden by conflict severity. */
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
 * Palette for the kinds that are not requests, in the same shape as {@link STATUS_SWATCH} so the
 * legend can read from one place. A booking borrows the "new" blue — it is scheduled work seen
 * from the resource's side — and an absence takes the neutral slate that already means
 * "not available" on the grids.
 */
export const KIND_SWATCH: Record<"assignment" | "absence", string> = {
  assignment: "bg-blue-500/15 dark:bg-blue-500/25 border-blue-500/40",
  absence: "bg-slate-500/15 dark:bg-slate-500/25 border-slate-400/40",
};

const KIND_EVENT_CLASS: Record<"assignment" | "absence", string[]> = {
  assignment: ["bg-blue-500/15!", "dark:bg-blue-500/25!", "border-blue-500/40!", "text-foreground!"],
  absence: ["bg-slate-500/15!", "dark:bg-slate-500/25!", "border-slate-400/40!", "text-muted-foreground!"],
};

/**
 * Event colour for a booking or an absence, on the same rules requests follow: the shared
 * `orkyo-cal-event` base (which owns the padding, radius and type scale that make the label
 * legible) and the same conflict-severity override.
 */
export function getKindEventClassNames(
  kind: "assignment" | "absence",
  severity: ConflictSeverity,
): string[] {
  if (severity) {
    return ["orkyo-cal-event", ...SEVERITY_EVENT_CLASS[severity]];
  }
  return ["orkyo-cal-event", ...KIND_EVENT_CLASS[kind]];
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
      kind: "request",
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
export type CalendarView =
  | "timeGridDay"
  | "timeGridWeek"
  | "dayGridMonth"
  | "listDay"
  | "listWeek"
  | "listMonth";

/**
 * Map the page's TimeScale to a FullCalendar view. The extremes collapse
 * (hour → day, year → month) — the calendar has no native hour/year view.
 * Phones render agenda-style list views instead of grids (a ~390px screen
 * can't fit an hour axis or a month grid).
 */
export function scaleToCalendarView(scale: string, opts?: { phone?: boolean }): CalendarView {
  const phone = opts?.phone ?? false;
  switch (scale) {
    case "day":
    case "hour":
      return phone ? "listDay" : "timeGridDay";
    case "week":
      return phone ? "listWeek" : "timeGridWeek";
    default:
      return phone ? "listMonth" : "dayGridMonth"; // month / year → month overview
  }
}
