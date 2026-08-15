import { describe, expect, it } from 'vitest';
import { EMPTY_CELL, formatListCell } from './format-list-cell';
import type { ListColumn } from '@foundation/src/lib/api/lists-api';

function column(overrides: Partial<ListColumn> = {}): ListColumn {
  return {
    id: 'c1',
    listDefinitionId: 'd1',
    key: 'k',
    label: 'K',
    dataType: 'text',
    isRequired: false,
    sortOrder: 0,
    isActive: true,
    createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z',
    ...overrides,
  };
}

describe('formatListCell', () => {
  it('shows an em dash for an unfilled cell, whatever the type', () => {
    for (const dataType of ['text', 'number', 'boolean', 'date', 'url', 'select'] as const) {
      expect(formatListCell(column({ dataType }), null)).toBe(EMPTY_CELL);
      expect(formatListCell(column({ dataType }), undefined)).toBe(EMPTY_CELL);
    }
  });

  it('reads a boolean as Yes or No rather than true or false', () => {
    expect(formatListCell(column({ dataType: 'boolean' }), true)).toBe('Yes');
    expect(formatListCell(column({ dataType: 'boolean' }), false)).toBe('No');
  });

  it('keeps a whitespace-only text cell an em dash', () => {
    expect(formatListCell(column(), '   ')).toBe(EMPTY_CELL);
  });

  it('renders the date the tenant typed, not the day before', () => {
    // A date column holds a plain calendar day. Parsing "2026-03-01" with new Date() reads it as
    // UTC midnight, which renders as February 28th anywhere west of UTC — this pins that the
    // day itself survives, whatever the runner's zone.
    const formatted = formatListCell(column({ dataType: 'date' }), '2026-03-01');

    expect(formatted).not.toBe(EMPTY_CELL);
    expect(formatted).toContain('2026');
    // The day-of-month is 1 in every locale ordering; the previous-day bug would show 28 or 29.
    expect(formatted).toMatch(/\b1\b/);
    expect(formatted).not.toMatch(/\b2[89]\b/);
  });

  it('passes a malformed date through rather than inventing one', () => {
    expect(formatListCell(column({ dataType: 'date' }), 'someday')).toBe('someday');
  });

  it('groups a large number for reading', () => {
    // Locale decides the separator; what matters is that it is not the bare digit run.
    expect(formatListCell(column({ dataType: 'number' }), 1234567)).not.toBe('1234567');
  });

  it('shows zero rather than treating it as unfilled', () => {
    expect(formatListCell(column({ dataType: 'number' }), 0)).toBe('0');
  });

  it('shows a false checkbox rather than treating it as unfilled', () => {
    expect(formatListCell(column({ dataType: 'boolean' }), false)).toBe('No');
  });
});
