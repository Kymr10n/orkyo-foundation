import { describe, it, expect, vi } from 'vitest';
import { HOURS, MINUTES_5, combineDateTimeFields, splitDateTimeFields } from './picker-utils';

describe('picker-utils', () => {
  describe('HOURS', () => {
    it('contains 24 entries', () => {
      expect(HOURS).toHaveLength(24);
    });

    it('starts at 00 and ends at 23', () => {
      expect(HOURS[0]).toBe('00');
      expect(HOURS[23]).toBe('23');
    });

    it('zero-pads single-digit hours', () => {
      expect(HOURS[5]).toBe('05');
      expect(HOURS[9]).toBe('09');
    });
  });

  describe('MINUTES_5', () => {
    it('contains 12 entries (0 to 55 in steps of 5)', () => {
      expect(MINUTES_5).toHaveLength(12);
    });

    it('starts at 00 and ends at 55', () => {
      expect(MINUTES_5[0]).toBe('00');
      expect(MINUTES_5[11]).toBe('55');
    });

    it('includes 30', () => {
      expect(MINUTES_5).toContain('30');
    });
  });

  describe('combineDateTimeFields', () => {
    it('combines date and time into ISO-like format', () => {
      expect(combineDateTimeFields('2026-04-17', '14:30')).toBe('2026-04-17T14:30');
    });

    it('returns empty string when date is empty', () => {
      expect(combineDateTimeFields('', '14:30')).toBe('');
    });

    it('defaults time to 08:00 when time is empty', () => {
      expect(combineDateTimeFields('2026-04-17', '')).toBe('2026-04-17T08:00');
    });

    it('defaults time to 08:00 when time is falsy', () => {
      expect(combineDateTimeFields('2026-01-01', '')).toBe('2026-01-01T08:00');
    });
  });

  describe('splitDateTimeFields', () => {
    it('splits a combined value into date and time', () => {
      const setDate = vi.fn();
      const setTime = vi.fn();
      splitDateTimeFields('2026-04-17T14:30', setDate, setTime);
      expect(setDate).toHaveBeenCalledWith('2026-04-17');
      expect(setTime).toHaveBeenCalledWith('14:30');
    });

    it('clears both fields when value is empty', () => {
      const setDate = vi.fn();
      const setTime = vi.fn();
      splitDateTimeFields('', setDate, setTime);
      expect(setDate).toHaveBeenCalledWith('');
      expect(setTime).toHaveBeenCalledWith('');
    });

    it('handles value without time component', () => {
      const setDate = vi.fn();
      const setTime = vi.fn();
      splitDateTimeFields('2026-04-17', setDate, setTime);
      expect(setDate).toHaveBeenCalledWith('2026-04-17');
      expect(setTime).toHaveBeenCalledWith(undefined);
    });
  });
});
