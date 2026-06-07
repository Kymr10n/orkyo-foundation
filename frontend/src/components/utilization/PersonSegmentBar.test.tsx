import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { PersonSegmentBar } from './PersonSegmentBar';
import type { PersonUtilizationSegment } from '@foundation/src/domain/scheduling/utilization-segments';

const VIEW_START = new Date('2026-01-06T08:00:00Z').getTime();
const VIEW_END = new Date('2026-01-06T11:00:00Z').getTime(); // 3-hour window

function seg(overrides: Partial<PersonUtilizationSegment> = {}): PersonUtilizationSegment {
  return {
    start: '2026-01-06T08:00:00Z',
    end: '2026-01-06T11:00:00Z',
    status: 'partial',
    utilizationPercent: 65,
    sourceUnitCount: 3,
    ...overrides,
  };
}

function renderBar(
  segment: PersonUtilizationSegment,
  onClick = vi.fn(),
  assignmentCount?: number,
) {
  render(
    <PersonSegmentBar
      segment={segment}
      personName="Alice Smith"
      viewStartMs={VIEW_START}
      viewEndMs={VIEW_END}
      assignmentCount={assignmentCount}
      onClick={onClick}
    />,
  );
  return onClick;
}

describe('PersonSegmentBar', () => {
  it('renders a status-coloured bar with the data-status attribute', () => {
    renderBar(seg({ status: 'overbooked', utilizationPercent: 125 }));
    const bar = screen.getByTestId('person-segment-bar');
    expect(bar).toHaveAttribute('data-status', 'overbooked');
    expect(bar.className).toMatch(/bg-red-100/);
  });

  it('labels a partial segment "Booked" with its utilization percent', () => {
    renderBar(seg({ status: 'partial', utilizationPercent: 65 }));
    expect(screen.getByText('Booked 65%')).toBeInTheDocument();
  });

  it('labels non-working segments "Off" without a percent', () => {
    renderBar(seg({ status: 'non-working' }));
    expect(screen.getByText('Off')).toBeInTheDocument();
  });

  it('fires onClick with the segment when clicked', () => {
    const s = seg();
    const onClick = renderBar(s);
    fireEvent.click(screen.getByTestId('person-segment-bar'));
    expect(onClick).toHaveBeenCalledWith(s);
  });

  it.each(['Enter', ' '])('fires onClick on %s key', (key) => {
    const s = seg();
    const onClick = renderBar(s);
    fireEvent.keyDown(screen.getByTestId('person-segment-bar'), { key });
    expect(onClick).toHaveBeenCalledWith(s);
  });

  it('is keyboard-focusable as a button (role + tabIndex)', () => {
    renderBar(seg());
    const bar = screen.getByTestId('person-segment-bar');
    expect(bar).toHaveAttribute('role', 'button');
    expect(bar).toHaveAttribute('tabindex', '0');
  });

  it('renders NO drag or resize affordances (read-only)', () => {
    renderBar(seg());
    const bar = screen.getByTestId('person-segment-bar');
    expect(bar).not.toHaveAttribute('draggable', 'true');
    expect(bar.className).toContain('cursor-pointer');
    expect(bar.className).not.toContain('cursor-grab');
    expect(bar.className).not.toContain('cursor-ew-resize');
  });

  it('hides the inline label for a narrow segment but keeps the tooltip', () => {
    // 6-minute segment in a 3-hour window → ~3.3% width, below the label threshold.
    renderBar(
      seg({ start: '2026-01-06T08:00:00Z', end: '2026-01-06T08:06:00Z', status: 'partial' }),
    );
    expect(screen.queryByText(/Booked/)).not.toBeInTheDocument();
    expect(screen.getByTestId('person-segment-bar')).toHaveAttribute('title');
  });

  it('renders nothing when the segment is entirely outside the window', () => {
    renderBar(seg({ start: '2026-01-06T12:00:00Z', end: '2026-01-06T13:00:00Z' }));
    expect(screen.queryByTestId('person-segment-bar')).not.toBeInTheDocument();
  });

  it('fills the meter to the utilization percent for a booked segment', () => {
    renderBar(seg({ status: 'partial', utilizationPercent: 65 }));
    const fill = screen.getByTestId('segment-fill');
    expect(fill).toHaveStyle({ width: '65%' });
    expect(fill.className).toMatch(/bg-amber-400/);
  });

  it('fills the meter fully and red for an overbooked segment', () => {
    renderBar(seg({ status: 'overbooked', utilizationPercent: 120 }));
    const fill = screen.getByTestId('segment-fill');
    expect(fill).toHaveStyle({ width: '100%' });
    expect(fill.className).toMatch(/bg-red-500/);
  });

  it('fills the meter fully for an assigned (exclusive) segment', () => {
    renderBar(seg({ status: 'assigned', utilizationPercent: 80 }));
    expect(screen.getByTestId('segment-fill')).toHaveStyle({ width: '100%' });
  });

  it('renders no meter fill for an available segment', () => {
    renderBar(seg({ status: 'available', utilizationPercent: 0 }));
    expect(screen.queryByTestId('segment-fill')).not.toBeInTheDocument();
  });

  it('renders no meter fill for a non-working segment', () => {
    renderBar(seg({ status: 'non-working' }));
    expect(screen.queryByTestId('segment-fill')).not.toBeInTheDocument();
  });

  it('shows the assignment-count badge when there are assignments', () => {
    renderBar(seg(), vi.fn(), 3);
    expect(screen.getByTestId('assignment-count-badge')).toHaveTextContent('3');
  });

  it('omits the badge when there are no assignments', () => {
    renderBar(seg(), vi.fn(), 0);
    expect(screen.queryByTestId('assignment-count-badge')).not.toBeInTheDocument();
  });

  it('omits the badge when assignmentCount is not provided', () => {
    renderBar(seg());
    expect(screen.queryByTestId('assignment-count-badge')).not.toBeInTheDocument();
  });

  it('pluralises the assignment count in the tooltip', () => {
    renderBar(seg(), vi.fn(), 1);
    expect(screen.getByTestId('person-segment-bar').getAttribute('title')).toContain(
      '1 assignment',
    );
  });

  it('uses the plural form for multiple assignments in the tooltip', () => {
    renderBar(seg(), vi.fn(), 2);
    expect(screen.getByTestId('person-segment-bar').getAttribute('title')).toContain(
      '2 assignments',
    );
  });
});
