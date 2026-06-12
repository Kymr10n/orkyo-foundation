import { describe, it, expect } from 'vitest';
import { enrichColumnsWithOffTime } from './time-grid-offtime';
import type { TimeColumn } from './scheduler-types';
import type { OffTimeRange } from '@foundation/src/domain/scheduling/types';

function col(startISO: string, endISO: string): TimeColumn {
  return { start: new Date(startISO), end: new Date(endISO), label: startISO };
}
function range(
  startISO: string,
  endISO: string,
  resourceIds: string[] | null,
): OffTimeRange {
  return {
    id: `${startISO}-${endISO}`,
    startMs: new Date(startISO).getTime(),
    endMs: new Date(endISO).getTime(),
    title: 'off',
    resourceIds,
  };
}

describe('enrichColumnsWithOffTime', () => {
  const columns = [
    col('2026-06-01T00:00:00Z', '2026-06-02T00:00:00Z'),
    col('2026-06-02T00:00:00Z', '2026-06-03T00:00:00Z'),
  ];

  it('returns the same array reference when no range is site-wide', () => {
    const perResource = [range('2026-06-01T00:00:00Z', '2026-06-02T00:00:00Z', ['r1'])];
    expect(enrichColumnsWithOffTime(columns, perResource)).toBe(columns);
  });

  it('returns the same reference when there are no ranges at all', () => {
    expect(enrichColumnsWithOffTime(columns, [])).toBe(columns);
  });

  it('flags only the columns fully covered by a site-wide range', () => {
    const siteWide = [range('2026-06-01T00:00:00Z', '2026-06-02T00:00:00Z', null)];
    const result = enrichColumnsWithOffTime(columns, siteWide);
    expect(result).not.toBe(columns);
    expect(result[0].isGlobalOffTime).toBe(true);
    expect(result[1].isGlobalOffTime).toBeUndefined();
  });

  it('does not flag a column only partially covered', () => {
    const partial = [range('2026-06-01T06:00:00Z', '2026-06-01T12:00:00Z', null)];
    const result = enrichColumnsWithOffTime(columns, partial);
    expect(result[0].isGlobalOffTime).toBeUndefined();
  });
});
