import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent, within } from '@testing-library/react';
import { PersonTimelineRow } from './PersonTimelineRow';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';
import type { ResourceAssignmentInfo } from '@foundation/src/lib/api/resource-assignments-api';
import type { PersonUtilizationSegment } from '@foundation/src/domain/scheduling/utilization-segments';

const VIEW_START = new Date('2026-01-06T08:00:00Z').getTime();
const VIEW_END = new Date('2026-01-06T11:00:00Z').getTime();

const person: ResourceInfo = {
  id: 'p-1',
  name: 'Alice Smith',
  resourceTypeId: 'rt-person',
  resourceTypeKey: 'person',
  allocationMode: 'Exclusive',
  baseAvailabilityPercent: 100,
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

const segments: PersonUtilizationSegment[] = [
  { start: '2026-01-06T08:00:00Z', end: '2026-01-06T09:00:00Z', status: 'available', utilizationPercent: 0, sourceUnitCount: 1 },
  { start: '2026-01-06T09:00:00Z', end: '2026-01-06T10:00:00Z', status: 'overbooked', utilizationPercent: 120, sourceUnitCount: 1 },
];

function renderRow(props: Partial<React.ComponentProps<typeof PersonTimelineRow>> = {}) {
  const onSegmentClick = vi.fn();
  render(
    <PersonTimelineRow
      person={person}
      jobTitle="Engineer"
      segments={segments}
      isLoadingRow={false}
      overallPct={60}
      viewStartMs={VIEW_START}
      viewEndMs={VIEW_END}
      columns={[]}
      onSegmentClick={onSegmentClick}
      {...props}
    />,
  );
  return onSegmentClick;
}

describe('PersonTimelineRow', () => {
  it('renders the label cell with name, job title and overall %', () => {
    renderRow();
    expect(screen.getByText('Alice Smith')).toBeInTheDocument();
    expect(screen.getByText('Engineer')).toBeInTheDocument();
    expect(screen.getByText('60%')).toBeInTheDocument();
  });

  it('renders one bar per visible segment', () => {
    renderRow();
    expect(screen.getAllByTestId('person-segment-bar')).toHaveLength(2);
  });

  it('forwards segment clicks with the person and the clicked segment', () => {
    const onSegmentClick = renderRow();
    const bars = screen.getAllByTestId('person-segment-bar');
    fireEvent.click(bars[1]);
    expect(onSegmentClick).toHaveBeenCalledWith(person, segments[1]);
  });

  it('shows the loading state', () => {
    renderRow({ isLoadingRow: true });
    expect(screen.getByText('Loading…')).toBeInTheDocument();
    expect(screen.queryByTestId('person-segment-bar')).not.toBeInTheDocument();
  });

  it('shows the empty state when there are no segments', () => {
    renderRow({ segments: [] });
    expect(screen.getByText('No data')).toBeInTheDocument();
  });

  it('badges each segment with the count of assignments overlapping it', () => {
    function asgn(start: string, end: string, id: string): ResourceAssignmentInfo {
      return {
        id,
        requestId: `req-${id}`,
        resourceId: person.id,
        resourceTypeKey: 'person',
        startUtc: start,
        endUtc: end,
        assignmentStatus: 'active',
        createdAt: start,
        updatedAt: start,
      };
    }
    // seg[0] 08:00–09:00, seg[1] 09:00–10:00 (half-open).
    const assignments = [
      asgn('2026-01-06T08:00:00Z', '2026-01-06T09:00:00Z', 'a1'), // seg[0] only
      asgn('2026-01-06T08:30:00Z', '2026-01-06T09:30:00Z', 'a2'), // both
      asgn('2026-01-06T10:00:00Z', '2026-01-06T11:00:00Z', 'a3'), // neither (touches seg[1] end)
    ];
    renderRow({ assignments });

    const bars = screen.getAllByTestId('person-segment-bar');
    expect(within(bars[0]).getByTestId('assignment-count-badge')).toHaveTextContent('2');
    expect(within(bars[1]).getByTestId('assignment-count-badge')).toHaveTextContent('1');
  });

  it('shows no badge on a segment with no overlapping assignments', () => {
    renderRow({
      assignments: [
        {
          id: 'a1',
          requestId: 'req-1',
          resourceId: person.id,
          resourceTypeKey: 'person',
          startUtc: '2026-01-06T20:00:00Z',
          endUtc: '2026-01-06T21:00:00Z',
          assignmentStatus: 'active',
          createdAt: '2026-01-06T20:00:00Z',
          updatedAt: '2026-01-06T20:00:00Z',
        },
      ],
    });
    expect(screen.queryByTestId('assignment-count-badge')).not.toBeInTheDocument();
  });

  // ── capability-conflict badge ─────────────────────────────────────────────

  it('shows the conflict badge on a segment whose overlapping assignment is conflicted', () => {
    const conflictedAssignment: ResourceAssignmentInfo = {
      id: 'conflict-a1',
      requestId: 'req-conflict',
      resourceId: person.id,
      resourceTypeKey: 'person',
      startUtc: '2026-01-06T08:30:00Z',
      endUtc: '2026-01-06T09:30:00Z', // overlaps both segments
      assignmentStatus: 'active',
      createdAt: '2026-01-06T08:00:00Z',
      updatedAt: '2026-01-06T08:00:00Z',
    };
    renderRow({
      assignments: [conflictedAssignment],
      conflictedAssignmentIds: new Set(['conflict-a1']),
    });
    const bars = screen.getAllByTestId('person-segment-bar');
    expect(within(bars[0]).getByTestId('capability-conflict-badge')).toBeInTheDocument();
  });

  it('does not show the conflict badge when conflictedAssignmentIds is empty', () => {
    const assignment: ResourceAssignmentInfo = {
      id: 'a-no-conflict',
      requestId: 'req-1',
      resourceId: person.id,
      resourceTypeKey: 'person',
      startUtc: '2026-01-06T08:00:00Z',
      endUtc: '2026-01-06T09:00:00Z',
      assignmentStatus: 'active',
      createdAt: '2026-01-06T08:00:00Z',
      updatedAt: '2026-01-06T08:00:00Z',
    };
    renderRow({
      assignments: [assignment],
      conflictedAssignmentIds: new Set(),
    });
    expect(screen.queryByTestId('capability-conflict-badge')).not.toBeInTheDocument();
  });

  it('does not show the conflict badge when the conflicted assignment does not overlap the segment', () => {
    const farAssignment: ResourceAssignmentInfo = {
      id: 'far-a1',
      requestId: 'req-far',
      resourceId: person.id,
      resourceTypeKey: 'person',
      startUtc: '2026-01-06T20:00:00Z', // well outside the view window
      endUtc: '2026-01-06T21:00:00Z',
      assignmentStatus: 'active',
      createdAt: '2026-01-06T08:00:00Z',
      updatedAt: '2026-01-06T08:00:00Z',
    };
    renderRow({
      assignments: [farAssignment],
      conflictedAssignmentIds: new Set(['far-a1']),
    });
    expect(screen.queryByTestId('capability-conflict-badge')).not.toBeInTheDocument();
  });
});
