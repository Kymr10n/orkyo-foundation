import type { Conflict, Request, RequestStatus } from '@foundation/src/types/requests';
import { getEventConflictSeverity, type CalendarEvent } from './request-calendar-events';

/** What a filter can say about a request's conflicts. Mirrors the legend's second half. */
export const ISSUE_FILTER = {
  ERROR: 'error',
  WARNING: 'warning',
  NONE: 'none',
} as const;

export type IssueFilter = (typeof ISSUE_FILTER)[keyof typeof ISSUE_FILTER];

export const ISSUE_FILTER_ORDER: IssueFilter[] = [
  ISSUE_FILTER.ERROR,
  ISSUE_FILTER.WARNING,
  ISSUE_FILTER.NONE,
];

export interface ScheduleFilter {
  /** Matched against the event title, case-insensitively. Empty matches everything. */
  query: string;
  statuses: readonly RequestStatus[];
  issues: readonly IssueFilter[];
}

/** The three things a filter judges, whatever surface the thing is drawn on. */
export interface FilterableSchedulable {
  name: string;
  /** Absences and assignments have no request status; a status filter cannot match them. */
  status: RequestStatus | undefined;
  issue: IssueFilter;
}

/**
 * Whether one scheduled thing survives the filter.
 *
 * An empty `statuses` or `issues` set means everything rather than nothing: a filter that can
 * empty the view is a way to look at a blank week and conclude the schedule is gone. The toolbar
 * never sends an empty set either, so this is the second of two guards on the same rule.
 */
export function matchesScheduleFilter(
  { name, status, issue }: FilterableSchedulable,
  { query, statuses, issues }: ScheduleFilter,
): boolean {
  const needle = query.trim().toLowerCase();

  if (needle && !name.toLowerCase().includes(needle)) return false;
  // A status filter constrains things that have a status. An absence or a booking is not a
  // request, so it is not what this filter describes — excluding it would empty the view of
  // everything the caller came to see.
  if (statuses.length > 0 && status !== undefined && !statuses.includes(status)) return false;
  if (issues.length > 0 && !issues.includes(issue)) return false;

  return true;
}

/** Calendar adapter — an event carries its status and severity in extendedProps. */
export function filterCalendarEvents(
  events: readonly CalendarEvent[],
  filter: ScheduleFilter,
): CalendarEvent[] {
  return events.filter((event) =>
    matchesScheduleFilter(
      {
        name: event.title,
        status: event.extendedProps.status,
        issue: event.extendedProps.conflictSeverity ?? ISSUE_FILTER.NONE,
      },
      filter,
    ),
  );
}

/**
 * Grid adapter — a request carries its own status, and its severity comes from the page's
 * conflict registry rather than from the request itself.
 */
export function filterScheduledRequests(
  requests: readonly Request[],
  filter: ScheduleFilter,
  conflicts: Map<string, Conflict[]>,
): Request[] {
  return requests.filter((request) =>
    matchesScheduleFilter(
      {
        name: request.name,
        status: request.status,
        issue: getEventConflictSeverity(request.id, conflicts) ?? ISSUE_FILTER.NONE,
      },
      filter,
    ),
  );
}
