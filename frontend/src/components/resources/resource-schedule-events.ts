import {
  getEventConflictSeverity,
  getKindEventClassNames,
  type CalendarEvent,
} from "@foundation/src/components/utilization/request-calendar-events";
import type { ResourceAbsenceInfo } from "@foundation/src/lib/api/resource-absences-api";
import type { ResourceAssignmentInfo } from "@foundation/src/lib/api/resource-assignments-api";
import type { Conflict, Request } from "@foundation/src/types/requests";

/**
 * Projection of one resource's own time onto the shared calendar: the work booked on it, and the
 * periods it is unavailable.
 *
 * Kept beside the request projection rather than inside it — the shapes share a target type, not a
 * source domain, and folding them together would make `request-calendar-events` import the absence
 * and assignment APIs for no reader's benefit. Colours, the conflict overlay and the
 * `orkyo-cal-event` base all come from that module, so this file decides nothing about how an
 * event looks.
 */

/** A booking is coloured by the conflicts of the request it books, exactly as the board is. */
export function assignmentToCalendarEvent(
  assignment: ResourceAssignmentInfo,
  requestsById: Map<string, Request>,
  conflicts: Map<string, Conflict[]>,
  editable: boolean,
): CalendarEvent {
  const request = requestsById.get(assignment.requestId);
  const severity = getEventConflictSeverity(assignment.requestId, conflicts);
  return {
    id: assignment.id,
    title: request?.name ?? "Booked",
    start: assignment.startUtc,
    end: assignment.endUtc,
    classNames: getKindEventClassNames("assignment", severity),
    editable,
    extendedProps: {
      kind: "assignment",
      requestId: assignment.requestId,
      // No status: a booking is not a request, and a status filter must not hide it.
      conflictSeverity: severity,
    },
  };
}

export function absenceToCalendarEvent(
  absence: ResourceAbsenceInfo,
  editable: boolean,
): CalendarEvent {
  return {
    id: absence.id,
    title: absence.title || absence.absenceType,
    start: absence.startTs,
    end: absence.endTs,
    // An absence is a statement about the resource, not a placement, so it carries no conflict.
    classNames: getKindEventClassNames("absence", null),
    // A disabled absence is shown for context but blocks nothing, so it is not draggable:
    // moving it would imply an effect it does not have.
    editable: editable && absence.enabled,
    extendedProps: { kind: "absence", conflictSeverity: null },
  };
}

/** Everything on one resource's calendar, absences last so a booking stays clickable above them. */
export function resourceScheduleEvents(
  assignments: readonly ResourceAssignmentInfo[],
  absences: readonly ResourceAbsenceInfo[],
  requestsById: Map<string, Request>,
  conflicts: Map<string, Conflict[]>,
  editable: boolean,
): CalendarEvent[] {
  return [
    ...absences.map((absence) => absenceToCalendarEvent(absence, editable)),
    ...assignments.map((a) => assignmentToCalendarEvent(a, requestsById, conflicts, editable)),
  ];
}
