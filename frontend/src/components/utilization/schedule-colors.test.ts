import { describe, it, expect } from 'vitest';
import {
  STATUS_CELL_CLASS,
  STATUS_BORDER_CLASS,
  SPACE_CANVAS_COLORS,
  SPACE_LEGEND_CELL_CLASS,
  SPACE_LEGEND_BORDER_CLASS,
  type BucketStatus,
  type SpaceStatus,
} from './schedule-colors';

const BUCKET_STATUSES: BucketStatus[] = [
  'available',
  'partial',
  'assigned',
  'overbooked',
  'non-working',
];
const SPACE_STATUSES: SpaceStatus[] = ['available', 'occupied', 'conflict'];

describe('schedule-colors', () => {
  it('STATUS_CELL_CLASS covers every BucketStatus', () => {
    for (const s of BUCKET_STATUSES) {
      expect(STATUS_CELL_CLASS[s], `missing STATUS_CELL_CLASS for "${s}"`).toBeTruthy();
    }
  });

  it('STATUS_BORDER_CLASS covers every BucketStatus', () => {
    for (const s of BUCKET_STATUSES) {
      expect(STATUS_BORDER_CLASS[s], `missing STATUS_BORDER_CLASS for "${s}"`).toBeTruthy();
    }
  });

  it('SPACE_CANVAS_COLORS has fill and stroke for every SpaceStatus', () => {
    for (const s of SPACE_STATUSES) {
      expect(SPACE_CANVAS_COLORS[s].fill, `missing fill for "${s}"`).toBeTruthy();
      expect(SPACE_CANVAS_COLORS[s].stroke, `missing stroke for "${s}"`).toBeTruthy();
    }
  });

  it('SPACE_LEGEND_CELL_CLASS delegates to STATUS_CELL_CLASS values', () => {
    const validValues = new Set(Object.values(STATUS_CELL_CLASS));
    for (const s of SPACE_STATUSES) {
      expect(
        validValues.has(SPACE_LEGEND_CELL_CLASS[s]),
        `SPACE_LEGEND_CELL_CLASS["${s}"] not in STATUS_CELL_CLASS`,
      ).toBe(true);
    }
  });

  it('SPACE_LEGEND_BORDER_CLASS delegates to STATUS_BORDER_CLASS values', () => {
    const validValues = new Set(Object.values(STATUS_BORDER_CLASS));
    for (const s of SPACE_STATUSES) {
      expect(
        validValues.has(SPACE_LEGEND_BORDER_CLASS[s]),
        `SPACE_LEGEND_BORDER_CLASS["${s}"] not in STATUS_BORDER_CLASS`,
      ).toBe(true);
    }
  });

  it('available space legend matches the available bucket color', () => {
    expect(SPACE_LEGEND_CELL_CLASS.available).toBe(STATUS_CELL_CLASS.available);
  });

  it('occupied space legend maps to the assigned bucket color', () => {
    expect(SPACE_LEGEND_CELL_CLASS.occupied).toBe(STATUS_CELL_CLASS.assigned);
  });

  it('conflict space legend maps to the overbooked bucket color', () => {
    expect(SPACE_LEGEND_CELL_CLASS.conflict).toBe(STATUS_CELL_CLASS.overbooked);
  });
});
