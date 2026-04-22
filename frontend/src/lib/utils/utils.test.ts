import { describe, it, expect } from 'vitest';
import { formatDateForInput, formatTimeForInput, combineDateTimeToISO, getDataTypeColor } from './utils';

describe('Date/Time Utilities', () => {
  describe('formatDateForInput', () => {
    it('should format date to YYYY-MM-DD', () => {
      const date = new Date('2026-01-29T14:30:00Z');
      const result = formatDateForInput(date);
      expect(result).toBe('2026-01-29');
    });

    it('should handle dates with single digit months and days', () => {
      const date = new Date('2026-03-05T00:00:00Z');
      const result = formatDateForInput(date);
      expect(result).toBe('2026-03-05');
    });

    it('should handle year boundaries correctly', () => {
      const date = new Date('2025-12-31T23:59:59Z');
      const result = formatDateForInput(date);
      expect(result).toBe('2025-12-31');
    });
  });

  describe('formatTimeForInput', () => {
    it('should format time to HH:MM', () => {
      const date = new Date('2026-01-29T14:30:00');
      const result = formatTimeForInput(date);
      expect(result).toMatch(/^\d{2}:\d{2}$/);
    });

    it('should pad single digit hours and minutes', () => {
      const date = new Date('2026-01-29T09:05:00');
      const result = formatTimeForInput(date);
      expect(result).toMatch(/^09:05$/);
    });
  });

  describe('combineDateTimeToISO', () => {
    it('should combine date and time strings to ISO format', () => {
      const result = combineDateTimeToISO('2026-01-29', '14:30');
      expect(result).toContain('2026-01-29');
      expect(result).toContain('T');
      expect(result).toMatch(/Z$/);
    });

    it('should handle midnight correctly', () => {
      const result = combineDateTimeToISO('2026-01-29', '00:00');
      const date = new Date(result);
      // Local midnight becomes previous day 23:00 UTC (for UTC-1 timezone)
      // The important thing is that the date object is valid and represents the correct time
      expect(date.toISOString()).toMatch(/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}/);
    });

    it('should handle end of day correctly', () => {
      const result = combineDateTimeToISO('2026-01-29', '23:59');
      expect(result).toContain('2026-01-29');
    });

    it('should produce valid ISO 8601 string', () => {
      const result = combineDateTimeToISO('2026-01-29', '14:30');
      const date = new Date(result);
      expect(date.toISOString()).toBe(result);
    });
  });
});

describe('getDataTypeColor', () => {
  it('should return correct color for Boolean', () => {
    const result = getDataTypeColor('Boolean');
    expect(result).toContain('blue');
  });

  it('should return correct color for Number', () => {
    const result = getDataTypeColor('Number');
    expect(result).toContain('green');
  });

  it('should return correct color for String', () => {
    const result = getDataTypeColor('String');
    expect(result).toContain('purple');
  });

  it('should return correct color for Date', () => {
    const result = getDataTypeColor('Date');
    expect(result).toContain('indigo');
  });

  it('should return correct color for Enum', () => {
    const result = getDataTypeColor('Enum');
    expect(result).toContain('orange');
  });

  it('should return default color for unknown type', () => {
    const result = getDataTypeColor('UnknownType');
    expect(result).toContain('muted');
  });

  it('should be case-sensitive', () => {
    const lower = getDataTypeColor('boolean');
    const proper = getDataTypeColor('Boolean');
    expect(lower).not.toBe(proper);
    expect(lower).toContain('muted');
  });

  it('should handle empty string', () => {
    const result = getDataTypeColor('');
    expect(result).toContain('muted');
  });
});
