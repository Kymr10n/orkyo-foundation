/**
 * Tests for the printed Utilization Gantt chart. The chart is rendered into a print-only
 * root in document.body, so the assertions read that DOM; pagination is the browser's and
 * is not asserted here.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { act } from 'react';
import { printGanttChart, PRINT_ROOT_ID, STATUS_COLOR } from './gantt-print';
import type { Request } from '@foundation/src/types/requests';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';
import { makeScheduledRequest } from '@foundation/src/test-utils/request-fixtures';
import { REQUEST_STATUS_ORDER } from '@foundation/src/constants/request-status';
import { formatStatusLabel } from '@foundation/src/lib/utils/utils';

/** Minimal ResourceTypeInfo — only key/displayNamePlural affect the chart. */
function type(key: string, displayNamePlural: string): ResourceTypeInfo {
  return {
    id: `type-${key}`,
    key,
    displayName: displayNamePlural.replace(/s$/, ''),
    displayNamePlural,
    hasGeometry: key === 'space',
    hasDirectoryProfile: key === 'person',
    singleGroupMembership: false,
    isSystem: true,
    isActive: true,
    createdAt: '2024-01-01',
    updatedAt: '2024-01-01',
  } as ResourceTypeInfo;
}

const SPACE_TYPE = type('space', 'Spaces');
const PERSON_TYPE = type('person', 'People');

const root = () => document.getElementById(PRINT_ROOT_ID)!;
const sections = () => Array.from(root().querySelectorAll<HTMLElement>('section.gp-section'));
const rowsOf = (section: HTMLElement) => Array.from(section.querySelectorAll<HTMLElement>('tbody tr'));
const barsOf = (el: HTMLElement) => Array.from(el.querySelectorAll<HTMLElement>('.gp-bar'));
const pct = (value: string) => parseFloat(value.replace('%', ''));

describe('gantt-print', () => {
  // The chart labels rows from a resourceId → {name, typeKey} map covering every
  // resource type, not just spaces.
  const mockResources = new Map([
    ['space-1', { name: 'Conference Room A', typeKey: 'space' }],
    ['space-2', { name: 'Conference Room B', typeKey: 'space' }],
    ['person-1', { name: 'Ada Heaney', typeKey: 'person' }],
  ]);

  // Assignments carry their own windows (that is what the chart draws), so the
  // fixture must set startUtc/endUtc explicitly — the fixture default of
  // 2026-01-01 would fall outside this window and be filtered out.
  const mockRequests: Request[] = [
    makeScheduledRequest('space-1', '2024-03-01T10:00:00Z', '2024-03-01T11:00:00Z', {
      id: 'req-1',
      name: 'Meeting 1',
      status: 'new',
    }),
    makeScheduledRequest('space-2', '2024-03-02T14:00:00Z', '2024-03-02T15:00:00Z', {
      id: 'req-2',
      name: 'Meeting 2',
      status: 'in_progress',
    }),
    makeScheduledRequest('space-1', '', '', {
      id: 'req-3',
      name: 'Unscheduled',
      assignments: [],
      startTs: null,
      endTs: null,
      status: 'new',
    }),
  ];

  // 30 days: 2024-03-01T00:00Z … 2024-03-31T00:00Z.
  const startDate = new Date('2024-03-01');
  const endDate = new Date('2024-03-31');
  const WINDOW_HOURS = 30 * 24;

  const print = (overrides: Partial<Parameters<typeof printGanttChart>[0]> = {}) =>
    act(() =>
      printGanttChart({
        requests: mockRequests,
        resources: mockResources,
        resourceTypes: [SPACE_TYPE],
        startDate,
        endDate,
        ...overrides,
      }),
    );

  /** A scheduled request assigned to a person-type resource. */
  function personRequest(resourceId: string, id: string, startUtc: string, endUtc: string): Request {
    const request = makeScheduledRequest(resourceId, startUtc, endUtc, { id, name: `Shift ${id}` });
    request.assignments[0].resourceTypeKey = 'person';
    return request;
  }

  const afterprint = () =>
    act(() => {
      window.dispatchEvent(new Event('afterprint'));
    });
  let printSpy: ReturnType<typeof vi.fn<() => void>>;

  beforeEach(() => {
    printSpy = vi.fn<() => void>();
    window.print = printSpy;
  });

  afterEach(afterprint);

  it('renders into a print root and opens the print dialog once', () => {
    print();
    expect(root()).toBeTruthy();
    expect(printSpy).toHaveBeenCalledTimes(1);
  });

  it('removes the root after printing', () => {
    print();
    afterprint();
    expect(document.getElementById(PRINT_ROOT_ID)).toBeNull();
  });

  it('replaces a root left behind by an earlier print', () => {
    print();
    print();
    expect(document.querySelectorAll(`#${PRINT_ROOT_ID}`)).toHaveLength(1);
  });

  it('carries its own stylesheet, hidden on screen and A4 landscape in print', () => {
    print();
    const css = root().querySelector('style')!.textContent!;
    expect(css).toContain(`#${PRINT_ROOT_ID} { display: none; }`);
    expect(css).toContain('size: A4 landscape');
    expect(css).toContain(`body > *:not(#${PRINT_ROOT_ID}) { display: none !important; }`);
  });

  it('renders the header with the date range and a generated stamp', () => {
    print();
    const text = root().textContent!;
    expect(text).toContain('Utilization Gantt Chart');
    expect(text).toMatch(/Period: .*Mar.*2024.*-.*Mar.*2024/);
    expect(text).toContain('Generated:');
  });

  it('renders the legend with every status label and colour', () => {
    print();
    const items = Array.from(root().querySelectorAll('.gp-legend li'));
    // Every status, in canonical order, with the shared label — a renamed status cannot
    // drop out of the legend.
    expect(items.map((li) => li.textContent)).toEqual(REQUEST_STATUS_ORDER.map(formatStatusLabel));
    expect(items.map((li) => li.querySelector<HTMLElement>('.gp-swatch')!.style.background)).toEqual(
      REQUEST_STATUS_ORDER.map((s) => STATUS_COLOR[s]),
    );
  });

  it('renders a statistics line per section', () => {
    print();
    expect(root().querySelector('.gp-stats')!.textContent).toBe(
      'Scheduled requests: 2 · Resources: 2 · Period: 30 days',
    );
  });

  it('filters out unscheduled requests', () => {
    print();
    expect(root().textContent).not.toContain('Unscheduled');
    expect(barsOf(root()).map((b) => b.dataset.requestId)).toEqual(['req-1', 'req-2']);
  });

  it('colours bars by request status', () => {
    print();
    const byId = Object.fromEntries(barsOf(root()).map((b) => [b.dataset.requestId, b]));
    expect(byId['req-1'].style.background).toBe(STATUS_COLOR.new);
    expect(byId['req-2'].style.background).toBe(STATUS_COLOR.in_progress);
  });

  it('groups bars by resource, one row each, sorted by name', () => {
    print({
      resources: new Map([
        ['space-1', { name: 'Zeta Room', typeKey: 'space' }],
        ['space-2', { name: 'Alpha Room', typeKey: 'space' }],
      ]),
    });
    const rows = rowsOf(sections()[0]);
    expect(rows.map((r) => r.querySelector('.gp-label')!.textContent)).toEqual(['Alpha Room', 'Zeta Room']);
    expect(rows.map((r) => barsOf(r).map((b) => b.dataset.requestId))).toEqual([['req-2'], ['req-1']]);
  });

  it('labels a resource missing from the map as unknown', () => {
    print({ resources: new Map() });
    expect(root().textContent).toContain('Unknown resource');
  });

  it('positions bars by their share of the window', () => {
    print();
    const bar = barsOf(root()).find((b) => b.dataset.requestId === 'req-1')!;
    // 2024-03-01T10:00Z is 10 hours into a 720-hour window; one hour wide, floored.
    expect(pct(bar.style.left)).toBeCloseTo((10 / WINDOW_HOURS) * 100, 5);
    expect(pct(bar.style.width)).toBeCloseTo(0.3, 5);
  });

  it('clamps a bar straddling the window start to the chart edge', () => {
    print({
      requests: [
        makeScheduledRequest('space-1', '2024-02-28T00:00:00Z', '2024-03-03T00:00:00Z', {
          id: 'straddle',
          name: 'Straddles',
        }),
      ],
    });
    const bar = barsOf(root())[0];
    expect(pct(bar.style.left)).toBe(0);
    expect(pct(bar.style.width)).toBeCloseTo((2 / 30) * 100, 5);
  });

  it('labels a bar only when it is wide enough to read', () => {
    print({
      requests: [
        makeScheduledRequest('space-1', '2024-03-01T00:00:00Z', '2024-03-10T00:00:00Z', { id: 'wide', name: 'Wide one' }),
        makeScheduledRequest('space-2', '2024-03-01T00:00:00Z', '2024-03-01T06:00:00Z', { id: 'thin', name: 'Thin one' }),
      ],
    });
    const byId = Object.fromEntries(barsOf(root()).map((b) => [b.dataset.requestId, b]));
    expect(byId.wide.textContent).toBe('Wide one');
    expect(byId.thin.textContent).toBe('');
    expect(byId.thin.title).toBe('Thin one');
  });

  it('excludes resources whose assignments fall entirely outside the window', () => {
    print({
      requests: [
        makeScheduledRequest('space-1', '2024-03-05T10:00:00Z', '2024-03-05T11:00:00Z', { id: 'in', name: 'Inside' }),
        makeScheduledRequest('space-2', '2024-04-05T10:00:00Z', '2024-04-05T11:00:00Z', { id: 'out', name: 'Outside' }),
      ],
    });
    expect(rowsOf(sections()[0]).map((r) => r.dataset.resourceId)).toEqual(['space-1']);
  });

  it('draws one bar per assignment, on each assigned resource row', () => {
    const request = makeScheduledRequest('space-1', '2024-03-05T10:00:00Z', '2024-03-05T12:00:00Z', {
      id: 'multi',
      name: 'Two rooms',
    });
    request.assignments.push({
      ...request.assignments[0],
      id: 'assign-2',
      resourceId: 'space-2',
      startUtc: '2024-03-06T10:00:00Z',
      endUtc: '2024-03-06T12:00:00Z',
    });
    print({ requests: [request] });
    const rows = rowsOf(sections()[0]);
    expect(rows.map((r) => r.dataset.resourceId)).toEqual(['space-1', 'space-2']);
    expect(rows.every((r) => barsOf(r).length === 1)).toBe(true);
  });

  it('renders one section per type, in the order given, titled with the plural name', () => {
    print({
      requests: [...mockRequests, personRequest('person-1', 'p1', '2024-03-04T08:00:00Z', '2024-03-04T16:00:00Z')],
      resourceTypes: [PERSON_TYPE, SPACE_TYPE],
    });
    expect(sections().map((s) => s.dataset.typeKey)).toEqual(['person', 'space']);
    expect(sections().map((s) => s.querySelector('.gp-type')!.textContent)).toEqual(['People', 'Spaces']);
    expect(sections().map((s) => s.querySelector('.gp-stats')!.textContent)).toEqual([
      'Scheduled requests: 1 · Resources: 1 · Period: 30 days',
      'Scheduled requests: 2 · Resources: 2 · Period: 30 days',
    ]);
  });

  it('renders only the types it is given', () => {
    print({
      requests: [...mockRequests, personRequest('person-1', 'p1', '2024-03-04T08:00:00Z', '2024-03-04T16:00:00Z')],
      resourceTypes: [SPACE_TYPE],
    });
    expect(sections().map((s) => s.dataset.typeKey)).toEqual(['space']);
    expect(root().textContent).not.toContain('Ada Heaney');
  });

  it('skips a type with nothing scheduled rather than emitting an empty section', () => {
    print({ resourceTypes: [PERSON_TYPE, SPACE_TYPE] });
    expect(sections().map((s) => s.dataset.typeKey)).toEqual(['space']);
  });

  it('repeats the axis header through a thead so every printed page carries it', () => {
    print();
    const section = sections()[0];
    expect(section.querySelector('thead .gp-axis')).toBeTruthy();
    const labels = Array.from(section.querySelectorAll('thead .gp-axis span'));
    // ~10 ticks over 30 days: every 3 days, 0..30 inclusive.
    expect(labels).toHaveLength(11);
    expect(pct(/left: ([\d.]+%)/.exec(labels[0].getAttribute('style')!)![1])).toBe(0);
  });

  it('still renders the chrome when nothing is scheduled at all', () => {
    print({ requests: [] });
    expect(sections()).toHaveLength(1);
    expect(root().textContent).toContain('Utilization Gantt Chart');
    expect(root().textContent).toContain('Nothing is scheduled in this period.');
    expect(root().querySelector('.gp-legend')).toBeTruthy();
    expect(printSpy).toHaveBeenCalledTimes(1);
  });
});
