import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import { OffTimeOverlay } from './OffTimeOverlay';
import type { OffTimeRange } from '@/domain/scheduling/types';
import type { TimeColumn } from './scheduler-types';

function makeColumns(startMs: number, endMs: number, count: number): TimeColumn[] {
  const step = (endMs - startMs) / count;
  return Array.from({ length: count }, (_, i) => ({
    label: `Col ${i}`,
    start: new Date(startMs + i * step),
    end: new Date(startMs + (i + 1) * step),
  }));
}

const DAY_MS = 86_400_000;
const VIEW_START = new Date('2026-04-01T00:00:00Z').getTime();
const VIEW_END = VIEW_START + 7 * DAY_MS;

const baseColumns = makeColumns(VIEW_START, VIEW_END, 7);

const baseRange: OffTimeRange = {
  id: 'ot-1',
  title: 'Maintenance',
  startMs: VIEW_START + 2 * DAY_MS, // day 3
  endMs: VIEW_START + 4 * DAY_MS,   // day 5
  spaceIds: null,
};

describe('OffTimeOverlay', () => {
  it('renders an overlay within view bounds', () => {
    const { container } = render(
      <OffTimeOverlay offTime={baseRange} columns={baseColumns} spaceId="s-1" />,
    );
    const overlay = container.firstChild as HTMLElement;
    expect(overlay).not.toBeNull();
    expect(overlay.style.left).toContain('%');
    expect(overlay.style.width).toContain('%');
    expect(overlay.title).toBe('Maintenance');
  });

  it('renders nothing when off-time is outside view range', () => {
    const offTime: OffTimeRange = {
      ...baseRange,
      startMs: VIEW_END + DAY_MS,
      endMs: VIEW_END + 2 * DAY_MS,
    };
    const { container } = render(
      <OffTimeOverlay offTime={offTime} columns={baseColumns} spaceId="s-1" />,
    );
    expect(container.firstChild).toBeNull();
  });

  it('renders nothing when columns are empty', () => {
    const { container } = render(
      <OffTimeOverlay offTime={baseRange} columns={[]} spaceId="s-1" />,
    );
    expect(container.firstChild).toBeNull();
  });

  it('renders when spaceIds is null (applies to all spaces)', () => {
    const { container } = render(
      <OffTimeOverlay offTime={baseRange} columns={baseColumns} spaceId="any-space" />,
    );
    expect(container.firstChild).not.toBeNull();
  });

  it('renders when space is in the spaceIds list', () => {
    const offTime: OffTimeRange = { ...baseRange, spaceIds: ['s-1', 's-2'] };
    const { container } = render(
      <OffTimeOverlay offTime={offTime} columns={baseColumns} spaceId="s-1" />,
    );
    expect(container.firstChild).not.toBeNull();
  });

  it('renders nothing when space is NOT in the spaceIds list', () => {
    const offTime: OffTimeRange = { ...baseRange, spaceIds: ['s-1', 's-2'] };
    const { container } = render(
      <OffTimeOverlay offTime={offTime} columns={baseColumns} spaceId="s-99" />,
    );
    expect(container.firstChild).toBeNull();
  });

  it('clamps overlay to view bounds when off-time extends beyond', () => {
    const offTime: OffTimeRange = {
      ...baseRange,
      startMs: VIEW_START - DAY_MS,  // starts before view
      endMs: VIEW_END + DAY_MS,       // ends after view
    };
    const { container } = render(
      <OffTimeOverlay offTime={offTime} columns={baseColumns} spaceId="s-1" />,
    );
    const overlay = container.firstChild as HTMLElement;
    expect(overlay).not.toBeNull();
    // Clamped: starts at 0%, full width
    expect(overlay.style.left).toBe('0%');
    expect(overlay.style.width).toBe('100%');
  });

  it('calculates correct left/width percentages', () => {
    // Off-time covers days 3-5 of a 7-day view → left=2/7, width=2/7
    const { container } = render(
      <OffTimeOverlay offTime={baseRange} columns={baseColumns} spaceId="s-1" />,
    );
    const overlay = container.firstChild as HTMLElement;
    const expectedLeft = (2 / 7) * 100;
    const expectedWidth = (2 / 7) * 100;
    expect(parseFloat(overlay.style.left)).toBeCloseTo(expectedLeft, 1);
    expect(parseFloat(overlay.style.width)).toBeCloseTo(expectedWidth, 1);
  });
});
