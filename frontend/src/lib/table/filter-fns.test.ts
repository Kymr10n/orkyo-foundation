import { describe, expect, it } from 'vitest';
import type { Row, RowData, TableFeatures } from '@tanstack/table-core';
import { arrayOverlaps, dateBetween, flattenFacets, oneOf } from './filter-fns';

/** The fns only call row.getValue(columnId), so a stub row is all they need. */
const rowWith = (value: unknown) => ({ getValue: () => value }) as unknown as Row<TableFeatures, RowData>;

describe('oneOf', () => {
  it('passes rows whose value is checked and drops the rest', () => {
    expect(oneOf(rowWith('active'), 'status', ['active', 'pending'], () => {})).toBe(true);
    expect(oneOf(rowWith('expired'), 'status', ['active', 'pending'], () => {})).toBe(false);
  });

  it('treats an empty selection as no filter, not match-nothing', () => {
    // Unticking the last checkbox must not blank the table.
    expect(oneOf.autoRemove!([], undefined as never)).toBe(true);
    expect(oneOf.autoRemove!(['active'], undefined as never)).toBe(false);
  });
});

describe('arrayOverlaps', () => {
  it('matches on overlap, not equality', () => {
    expect(arrayOverlaps(rowWith(['space', 'person']), 'appliesTo', ['person'], () => {})).toBe(true);
    expect(arrayOverlaps(rowWith(['space']), 'appliesTo', ['person'], () => {})).toBe(false);
  });

  it('rejects non-array cells rather than throwing', () => {
    expect(arrayOverlaps(rowWith(null), 'appliesTo', ['person'], () => {})).toBe(false);
  });
});

describe('dateBetween', () => {
  const jan = '2026-01-15T10:00:00Z';

  it('honours open-ended bounds', () => {
    expect(dateBetween(rowWith(jan), 'created', ['2026-01-01', undefined], () => {})).toBe(true);
    expect(dateBetween(rowWith(jan), 'created', [undefined, '2026-01-31'], () => {})).toBe(true);
    expect(dateBetween(rowWith(jan), 'created', ['2026-02-01', undefined], () => {})).toBe(false);
  });

  it('includes the whole end day', () => {
    // "to 2026-01-15" must include an event at 10:00 that day, not cut off at midnight.
    expect(dateBetween(rowWith(jan), 'created', [undefined, '2026-01-15'], () => {})).toBe(true);
  });

  it('drops rows whose date does not parse', () => {
    // A null date ("never logged in") is not inside any range.
    expect(dateBetween(rowWith(''), 'lastLogin', ['2026-01-01', undefined], () => {})).toBe(false);
  });
});

describe('flattenFacets', () => {
  it('counts array cells per element instead of per whole array', () => {
    const faceted = new Map<unknown, number>([
      [['space', 'person'], 3],
      [['space'], 2],
      ['person', 1], // scalar keys pass through
    ]);

    const flat = flattenFacets(faceted);
    expect(flat.get('space')).toBe(5);
    expect(flat.get('person')).toBe(4);
  });
});
