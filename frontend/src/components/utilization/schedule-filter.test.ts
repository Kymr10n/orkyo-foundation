import { describe, it, expect } from 'vitest';
import { REQUEST_STATUS_ORDER } from '@foundation/src/constants/request-status';
import { ISSUE_FILTER, ISSUE_FILTER_ORDER, filterCalendarEvents } from './schedule-filter';
import type { CalendarEvent } from './request-calendar-events';
import type { RequestStatus } from '@foundation/src/types/requests';
import type { ConflictSeverity } from './request-calendar-events';

function event(
  title: string,
  status: RequestStatus = 'new',
  conflictSeverity: ConflictSeverity = null,
): CalendarEvent {
  return {
    id: title,
    title,
    start: '2026-08-17T09:00:00Z',
    end: '2026-08-17T10:00:00Z',
    classNames: [],
    editable: true,
    extendedProps: { requestId: title, status, conflictSeverity },
  };
}

const EVENTS = [
  event('Fabricate frame', 'new', 'error'),
  event('Finish weld', 'in_progress', 'warning'),
  event('Audit weld quality', 'done'),
  event('Pack customer order', 'cancelled'),
];

const ALL = {
  query: '',
  statuses: REQUEST_STATUS_ORDER,
  issues: ISSUE_FILTER_ORDER,
};

const titles = (events: CalendarEvent[]) => events.map((e) => e.title);

describe('filterCalendarEvents', () => {
  it('keeps everything when nothing is narrowed', () => {
    expect(filterCalendarEvents(EVENTS, ALL)).toHaveLength(4);
  });

  it('matches the title case-insensitively, anywhere in it', () => {
    expect(titles(filterCalendarEvents(EVENTS, { ...ALL, query: 'weld' }))).toEqual([
      'Finish weld',
      'Audit weld quality',
    ]);
  });

  it('ignores surrounding whitespace in the query', () => {
    expect(filterCalendarEvents(EVENTS, { ...ALL, query: '  frame  ' })).toHaveLength(1);
  });

  it('narrows to the chosen statuses', () => {
    expect(titles(filterCalendarEvents(EVENTS, { ...ALL, statuses: ['done', 'cancelled'] }))).toEqual([
      'Audit weld quality',
      'Pack customer order',
    ]);
  });

  it('narrows to the chosen issue levels', () => {
    expect(titles(filterCalendarEvents(EVENTS, { ...ALL, issues: [ISSUE_FILTER.ERROR] }))).toEqual([
      'Fabricate frame',
    ]);
  });

  it('treats an event with no conflicts as "no issues"', () => {
    expect(titles(filterCalendarEvents(EVENTS, { ...ALL, issues: [ISSUE_FILTER.NONE] }))).toEqual([
      'Audit weld quality',
      'Pack customer order',
    ]);
  });

  it('combines every criterion', () => {
    const result = filterCalendarEvents(EVENTS, {
      query: 'weld',
      statuses: ['in_progress'],
      issues: [ISSUE_FILTER.WARNING],
    });
    expect(titles(result)).toEqual(['Finish weld']);
  });

  it('reads an empty set as everything, never as nothing', () => {
    // A filter that can empty the calendar is a way to look at a blank week and conclude the
    // schedule is gone.
    expect(filterCalendarEvents(EVENTS, { ...ALL, statuses: [], issues: [] })).toHaveLength(4);
  });

  it('can legitimately match nothing when the query matches nothing', () => {
    expect(filterCalendarEvents(EVENTS, { ...ALL, query: 'nothing here' })).toHaveLength(0);
  });
});
