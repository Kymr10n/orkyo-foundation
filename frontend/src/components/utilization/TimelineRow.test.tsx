import { describe, it, expect, vi } from 'vitest';
import { render, fireEvent } from '@testing-library/react';
import { TimelineRow } from './TimelineRow';
import type { TimeColumn } from './scheduler-types';

const columns: TimeColumn[] = [
  { start: new Date('2026-01-01T08:00:00Z'), end: new Date('2026-01-01T09:00:00Z'), label: '08' },
  { start: new Date('2026-01-01T09:00:00Z'), end: new Date('2026-01-01T10:00:00Z'), label: '09' },
];

function getCells(container: HTMLElement): HTMLElement[] {
  return Array.from(container.querySelectorAll<HTMLElement>('[class*="min-w-[60px]"]'));
}

describe('TimelineRow cell interactivity', () => {
  it('renders plain presentational cells when onCellClick is absent', () => {
    const { container } = render(<TimelineRow rowId="r1" columns={columns} label="Row" />);
    for (const cell of getCells(container)) {
      expect(cell).not.toHaveAttribute('role');
      expect(cell).not.toHaveAttribute('tabindex');
    }
  });

  it('fires onCellClick with the clicked column', () => {
    const onCellClick = vi.fn();
    const { container } = render(
      <TimelineRow rowId="r1" columns={columns} label="Row" onCellClick={onCellClick} />,
    );
    fireEvent.click(getCells(container)[1]);
    expect(onCellClick).toHaveBeenCalledWith(columns[1]);
  });

  it('activates via Enter and Space and labels cells for the screen reader', () => {
    const onCellClick = vi.fn();
    const { container } = render(
      <TimelineRow
        rowId="r1"
        columns={columns}
        label="Row"
        onCellClick={onCellClick}
        cellAriaLabel={(col) => `Schedule at ${col.label}`}
      />,
    );
    const [first] = getCells(container);
    expect(first).toHaveAttribute('role', 'button');
    expect(first).toHaveAttribute('tabindex', '0');
    expect(first).toHaveAttribute('aria-label', 'Schedule at 08');
    fireEvent.keyDown(first, { key: 'Enter' });
    fireEvent.keyDown(first, { key: ' ' });
    expect(onCellClick).toHaveBeenCalledTimes(2);
    expect(onCellClick).toHaveBeenNthCalledWith(1, columns[0]);
  });
});

describe('TimelineRow off-time labelling', () => {
  // The reported symptom: a shaded, hatched column that answers no click and shows no
  // tooltip reads as a broken conflict block. Every shaded column now says why.
  const shaded: TimeColumn[] = [
    { start: new Date('2026-01-03T08:00:00Z'), end: new Date('2026-01-03T09:00:00Z'), label: 'Sat', isWeekend: true },
    { start: new Date('2026-01-05T20:00:00Z'), end: new Date('2026-01-05T21:00:00Z'), label: '20', isOutsideWorkingHours: true },
    { start: new Date('2026-01-06T08:00:00Z'), end: new Date('2026-01-06T09:00:00Z'), label: '08', isGlobalOffTime: true },
  ];

  it('explains why a non-clickable column is shaded', () => {
    const { container } = render(<TimelineRow rowId="r1" columns={shaded} label="Row" />);
    const cells = getCells(container);

    expect(cells[0]).toHaveAttribute('title', 'Weekend — outside working days');
    expect(cells[1]).toHaveAttribute('title', 'Outside working hours');
    expect(cells[2]).toHaveAttribute('title', 'Closed — holiday or shutdown');
  });

  it('names resource off-time ahead of the calendar reasons', () => {
    const { container } = render(
      <TimelineRow rowId="r1" columns={shaded} label="Row" isOffTime={() => true} />,
    );

    expect(getCells(container)[0]).toHaveAttribute('title', 'Off-time');
  });

  it('leaves ordinary working columns unlabelled', () => {
    const { container } = render(<TimelineRow rowId="r1" columns={columns} label="Row" />);

    for (const cell of getCells(container)) {
      expect(cell).not.toHaveAttribute('title');
    }
  });

  it('shades off-time neutrally, so red keeps meaning conflict', () => {
    const { container } = render(
      <TimelineRow rowId="r1" columns={shaded} label="Row" isOffTime={() => true} />,
    );

    // A destructive tint here put closed time and overbooking in one colour.
    expect(getCells(container)[0].className).not.toContain('--destructive');
    expect(getCells(container)[0].className).toContain('--muted-foreground');
  });
});
