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
