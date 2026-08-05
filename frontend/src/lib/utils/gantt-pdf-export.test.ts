/* eslint-disable @typescript-eslint/no-explicit-any */
/**
 * Tests for PDF Export functionality
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { exportGanttChartToPDF } from './gantt-pdf-export';
import type { Request } from '@foundation/src/types/requests';
import type { ResourceTypeInfo } from '@foundation/src/lib/api/resource-types-api';
import { makeScheduledRequest, spaceAssignment } from '@foundation/src/test-utils/request-fixtures';

// Create mock PDF instance
const mockPDFInstance = {
  internal: {
    pageSize: {
      getWidth: () => 297,
      getHeight: () => 210,
    },
  },
  setFontSize: vi.fn().mockReturnThis(),
  setFont: vi.fn().mockReturnThis(),
  text: vi.fn().mockReturnThis(),
  setDrawColor: vi.fn().mockReturnThis(),
  line: vi.fn().mockReturnThis(),
  setTextColor: vi.fn().mockReturnThis(),
  setFillColor: vi.fn().mockReturnThis(),
  roundedRect: vi.fn().mockReturnThis(),
  addPage: vi.fn().mockReturnThis(),
  save: vi.fn().mockReturnThis(),
};

// Mock jsPDF constructor
vi.mock('jspdf', () => {
  const mockConstructor = vi.fn(function(this: any) {
    return mockPDFInstance;
  });
  return {
    default: mockConstructor,
  };
});

/** All strings handed to doc.text(). */
function textCalls(): string[] {
  return mockPDFInstance.text.mock.calls.map((call: any) => call[0]);
}

/** roundedRect calls that drew request bars (radius 1; legend swatches use 0.5). */
function barCalls(): any[][] {
  return mockPDFInstance.roundedRect.mock.calls.filter((call: any[]) => call[4] === 1);
}

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

describe('gantt-pdf-export', () => {
  // The chart labels rows from a resourceId → {name, typeKey} map covering every
  // resource type, not just spaces.
  const mockResources = new Map([
    ['space-1', { name: 'Conference Room A', typeKey: 'space' }],
    ['space-2', { name: 'Conference Room B', typeKey: 'space' }],
    ['person-1', { name: 'Ada Heaney', typeKey: 'person' }],
  ]);

  // Assignments carry their own windows (that is what the exporter draws), so
  // the fixture must set startUtc/endUtc explicitly — the fixture default of
  // 2026-01-01 would fall outside this export window and be filtered out.
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

  const startDate = new Date('2024-03-01');
  const endDate = new Date('2024-03-31');

  const exportDefault = (overrides: Partial<Parameters<typeof exportGanttChartToPDF>[0]> = {}) =>
    exportGanttChartToPDF({
      requests: mockRequests,
      resources: mockResources,
      resourceTypes: [SPACE_TYPE],
      startDate,
      endDate,
      ...overrides,
    });

  /** n distinct resources of one type, each with one in-window scheduled request. */
  function requestsForResources(
    n: number,
    typeKey = 'space',
  ): { requests: Request[]; resources: Map<string, { name: string; typeKey: string }> } {
    const resources = new Map<string, { name: string; typeKey: string }>();
    const requests = Array.from({ length: n }, (_, i) => {
      const id = `${typeKey}-${i}`;
      resources.set(id, { name: `Room ${String(i).padStart(3, '0')}`, typeKey });
      const request = makeScheduledRequest(id, '2024-03-05T10:00:00Z', '2024-03-05T12:00:00Z', {
        id: `req-${typeKey}-${i}`,
        name: `Booking ${i}`,
      });
      request.assignments[0].resourceTypeKey = typeKey;
      return request;
    });
    return { requests, resources };
  }

  /** A scheduled request assigned to a person-type resource. */
  function personRequest(resourceId: string, id: string, startUtc: string, endUtc: string): Request {
    const request = makeScheduledRequest(resourceId, startUtc, endUtc, { id, name: `Shift ${id}` });
    request.assignments[0].resourceTypeKey = 'person';
    return request;
  }

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('should generate PDF without errors', () => {
    expect(() => exportDefault()).not.toThrow();
  });

  it('should filter out unscheduled requests', () => {
    exportDefault();

    // Only req-1 and req-2 draw bars; req-3 has no schedule and no assignments.
    expect(barCalls().length).toBe(2);
  });

  it('should use custom filename when provided', () => {
    const customFilename = 'custom-gantt.pdf';
    exportDefault({ filename: customFilename });

    expect(mockPDFInstance.save).toHaveBeenCalledWith(customFilename);
  });

  it('names a single-type export after that type', () => {
    exportDefault();

    expect(mockPDFInstance.save).toHaveBeenCalled();
    const savedFilename = mockPDFInstance.save.mock.calls[0][0];
    expect(savedFilename).toMatch(/^gantt-chart-space-\d{4}-\d{2}-\d{2}\.pdf$/);
  });

  it('leaves a multi-type export unscoped in the filename', () => {
    exportDefault({
      requests: [
        ...mockRequests,
        personRequest('person-1', 'req-p', '2024-03-04T10:00:00Z', '2024-03-04T12:00:00Z'),
      ],
      resourceTypes: [SPACE_TYPE, PERSON_TYPE],
    });

    expect(mockPDFInstance.save.mock.calls[0][0]).toMatch(/^gantt-chart-\d{4}-\d{2}-\d{2}\.pdf$/);
  });

  it('should handle empty request list', () => {
    expect(() => exportDefault({ requests: [] })).not.toThrow();
  });

  it('should handle an empty resource-name map', () => {
    expect(() => exportDefault({ resources: new Map() })).not.toThrow();
  });

  it('should render header with date range', () => {
    exportDefault();

    expect(mockPDFInstance.text).toHaveBeenCalledWith(
      'Utilization Gantt Chart',
      expect.any(Number),
      expect.any(Number)
    );
    expect(textCalls().some((text: string) => text.includes('Mar 1, 2024'))).toBe(true);
  });

  it('should render the legend strip with status labels', () => {
    exportDefault();

    const calls = textCalls();
    expect(calls).toContain('New');
    expect(calls).toContain('In Progress');
    expect(calls).toContain('Done');
    expect(calls).toContain('Deferred');
    expect(calls).toContain('Canceled'); // formatStatusLabel's spelling, not the wire value's
  });

  it('should render a statistics line on the first page', () => {
    exportDefault();

    expect(
      textCalls().some((text: string) =>
        text.includes('Scheduled requests: 2') && text.includes('Resources: 2'),
      ),
    ).toBe(true);
  });

  it('should apply correct colors for different request statuses', () => {
    exportDefault();

    expect(mockPDFInstance.setFillColor).toHaveBeenCalledWith(59, 130, 246); // blue for new
    expect(mockPDFInstance.setFillColor).toHaveBeenCalledWith(249, 115, 22); // orange for in_progress
  });

  it('should group requests by resource', () => {
    exportDefault();

    const calls = textCalls();
    expect(calls).toContain('Conference Room A');
    expect(calls).toContain('Conference Room B');
  });

  // ── Window filtering & clamping ───────────────────────────────────────────

  it('excludes resources whose assignments fall entirely outside the window', () => {
    exportDefault({
      requests: [
        ...mockRequests,
        // The page hands the exporter its whole buffered fetch window; this one
        // is from a month the PDF does not cover.
        makeScheduledRequest('person-1', '2024-05-10T10:00:00Z', '2024-05-10T12:00:00Z', {
          id: 'req-out',
          name: 'Out of window',
        }),
      ],
    });

    expect(textCalls()).not.toContain('Ada Heaney');
    expect(barCalls().length).toBe(2);
  });

  it('clamps a bar straddling the window start to the chart edge', () => {
    exportDefault({
      requests: [
        makeScheduledRequest('space-1', '2024-02-25T00:00:00Z', '2024-03-03T00:00:00Z', {
          id: 'req-straddle',
          name: 'Straddler',
        }),
      ],
    });

    const [bar] = barCalls();
    expect(bar[0]).toBe(60); // chartX = MARGIN 15 + LABEL_GUTTER 45
  });

  // ── Label gutter ──────────────────────────────────────────────────────────

  it('right-aligns resource labels into the gutter', () => {
    exportDefault();

    const labelCall = mockPDFInstance.text.mock.calls.find(
      (call: any[]) => call[0] === 'Conference Room A',
    )!;
    expect(labelCall[1]).toBe(57); // chartX − 3
    expect(labelCall[3]).toEqual({ align: 'right', maxWidth: 40 });
  });

  it('sorts rows by resource name', () => {
    exportDefault({
      requests: [
        makeScheduledRequest('space-2', '2024-03-02T14:00:00Z', '2024-03-02T15:00:00Z', { id: 'r1', name: 'B' }),
        makeScheduledRequest('space-1', '2024-03-01T10:00:00Z', '2024-03-01T11:00:00Z', { id: 'r2', name: 'A' }),
      ],
    });

    const calls = textCalls();
    expect(calls.indexOf('Conference Room A')).toBeLessThan(calls.indexOf('Conference Room B'));
  });

  // ── Pagination ────────────────────────────────────────────────────────────

  it('fits 20 rows on a single page', () => {
    const { requests, resources } = requestsForResources(20);
    exportDefault({ requests, resources });

    expect(mockPDFInstance.addPage).not.toHaveBeenCalled();
    expect(textCalls()).toContain('Page 1 of 1');
  });

  it('paginates at 21 rows', () => {
    const { requests, resources } = requestsForResources(21);
    exportDefault({ requests, resources });

    expect(mockPDFInstance.addPage).toHaveBeenCalledTimes(1);
    const calls = textCalls();
    expect(calls).toContain('Page 1 of 2');
    expect(calls).toContain('Page 2 of 2');
    // Every row is drawn exactly once across the pages.
    expect(barCalls().length).toBe(21);
  });

  it('paginates at the 40/41 boundary', () => {
    const { requests, resources } = requestsForResources(41);
    exportDefault({ requests, resources });

    expect(mockPDFInstance.addPage).toHaveBeenCalledTimes(2);
    expect(textCalls()).toContain('Page 3 of 3');
  });

  it('repeats the header on every page', () => {
    const { requests, resources } = requestsForResources(21);
    exportDefault({ requests, resources });

    const headerCalls = textCalls().filter((text: string) => text === 'Utilization Gantt Chart');
    expect(headerCalls.length).toBe(2);
  });

  // ── Per-type sections ─────────────────────────────────────────────────────

  it('renders only the types it is given', () => {
    // The Spaces tab exports spaces alone: a person assignment in the same
    // request set must not add a row.
    exportDefault({
      requests: [
        ...mockRequests,
        personRequest('person-1', 'req-p', '2024-03-04T10:00:00Z', '2024-03-04T12:00:00Z'),
      ],
      resourceTypes: [SPACE_TYPE],
    });

    const calls = textCalls();
    expect(calls).toContain('Conference Room A');
    expect(calls).not.toContain('Ada Heaney');
    expect(calls).not.toContain('People');
  });

  it('starts each type on its own page, titled with the plural name', () => {
    exportDefault({
      requests: [
        ...mockRequests,
        personRequest('person-1', 'req-p', '2024-03-04T10:00:00Z', '2024-03-04T12:00:00Z'),
      ],
      resourceTypes: [SPACE_TYPE, PERSON_TYPE],
    });

    // Two sections of one page each — people never share a page with spaces.
    expect(mockPDFInstance.addPage).toHaveBeenCalledTimes(1);
    const calls = textCalls();
    expect(calls).toContain('Spaces');
    expect(calls).toContain('People');
    expect(calls).toContain('Page 1 of 2');
    expect(calls).toContain('Page 2 of 2');
  });

  it('keeps section order as given, not alphabetical by resource', () => {
    exportDefault({
      requests: [
        ...mockRequests,
        personRequest('person-1', 'req-p', '2024-03-04T10:00:00Z', '2024-03-04T12:00:00Z'),
      ],
      resourceTypes: [PERSON_TYPE, SPACE_TYPE],
    });

    const calls = textCalls();
    // "Ada Heaney" sorts before "Conference Room A", but section order decides.
    expect(calls.indexOf('People')).toBeLessThan(calls.indexOf('Spaces'));
  });

  it('skips a type with nothing scheduled rather than emitting an empty page', () => {
    exportDefault({ resourceTypes: [SPACE_TYPE, PERSON_TYPE] });

    expect(mockPDFInstance.addPage).not.toHaveBeenCalled();
    const calls = textCalls();
    expect(calls).toContain('Page 1 of 1');
    expect(calls).not.toContain('People');
  });

  it('paginates each section independently', () => {
    const spaces = requestsForResources(21, 'space');
    const people = requestsForResources(3, 'person');

    exportDefault({
      requests: [...spaces.requests, ...people.requests],
      resources: new Map([...spaces.resources, ...people.resources]),
      resourceTypes: [SPACE_TYPE, PERSON_TYPE],
    });

    // Spaces: 20 + 1 rows = 2 pages. People: 3 rows = 1 page. Total 3.
    expect(mockPDFInstance.addPage).toHaveBeenCalledTimes(2);
    expect(textCalls()).toContain('Page 3 of 3');
  });

  it('scopes the statistics line to its section', () => {
    const spaces = requestsForResources(2, 'space');
    const people = requestsForResources(3, 'person');

    exportDefault({
      requests: [...spaces.requests, ...people.requests],
      resources: new Map([...spaces.resources, ...people.resources]),
      resourceTypes: [SPACE_TYPE, PERSON_TYPE],
    });

    const stats = textCalls().filter((t: string) => t.includes('Scheduled requests:'));
    expect(stats).toHaveLength(2);
    expect(stats[0]).toContain('Resources: 2');
    expect(stats[1]).toContain('Resources: 3');
  });

  it('still renders chrome when nothing is scheduled at all', () => {
    exportDefault({ requests: [], resourceTypes: [SPACE_TYPE, PERSON_TYPE] });

    const calls = textCalls();
    expect(calls).toContain('Utilization Gantt Chart');
    expect(calls).toContain('Page 1 of 1');
    expect(barCalls()).toHaveLength(0);
  });

  // ── Footer ────────────────────────────────────────────────────────────────

  it('renders the Orkyo footer, not the legacy typo', () => {
    exportDefault();

    const calls = textCalls();
    expect(calls).toContain('Orkyo');
    expect(calls).not.toContain('Utilzing Space');
  });

  // ── Assignment-level bars ─────────────────────────────────────────────────

  it('draws one bar per assignment, on each assigned resource row', () => {
    const request = makeScheduledRequest('space-1', '2024-03-05T10:00:00Z', '2024-03-05T12:00:00Z', {
      id: 'req-multi',
      name: 'Shared work',
    });
    request.assignments.push(
      spaceAssignment('space-2', { startUtc: '2024-03-06T10:00:00Z', endUtc: '2024-03-06T12:00:00Z' }),
    );

    exportDefault({ requests: [request] });

    // Two rows (one per resource), one bar each.
    expect(barCalls().length).toBe(2);
    const calls = textCalls();
    expect(calls).toContain('Conference Room A');
    expect(calls).toContain('Conference Room B');
  });
});
