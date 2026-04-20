import { describe, it, expect } from 'vitest';
import {
  MS_PER_SECOND,
  MS_PER_MINUTE,
  MS_PER_HOUR,
  MS_PER_DAY,
  MS_PER_WEEK,
  DURATION_TO_MINUTES,
  DURATION_UNIT_MS,
  isWeekendDay,
  SUNDAY,
  SATURDAY,
  RRULE_DAY_MAP,
} from './constants';

describe('domain/constants', () => {
  describe('time conversions', () => {
    it('MS_PER_SECOND is 1000', () => {
      expect(MS_PER_SECOND).toBe(1_000);
    });

    it('MS_PER_MINUTE is 60_000', () => {
      expect(MS_PER_MINUTE).toBe(60_000);
    });

    it('MS_PER_HOUR is 3_600_000', () => {
      expect(MS_PER_HOUR).toBe(3_600_000);
    });

    it('MS_PER_DAY is 86_400_000', () => {
      expect(MS_PER_DAY).toBe(86_400_000);
    });

    it('MS_PER_WEEK is 7 * MS_PER_DAY', () => {
      expect(MS_PER_WEEK).toBe(7 * MS_PER_DAY);
    });
  });

  describe('DURATION_TO_MINUTES', () => {
    it('minutes = 1', () => expect(DURATION_TO_MINUTES.minutes).toBe(1));
    it('hours = 60', () => expect(DURATION_TO_MINUTES.hours).toBe(60));
    it('days = 1440', () => expect(DURATION_TO_MINUTES.days).toBe(1440));
    it('weeks = 10080', () => expect(DURATION_TO_MINUTES.weeks).toBe(10_080));
  });

  describe('DURATION_UNIT_MS', () => {
    it('minutes = MS_PER_MINUTE', () => expect(DURATION_UNIT_MS.minutes).toBe(MS_PER_MINUTE));
    it('hours = MS_PER_HOUR', () => expect(DURATION_UNIT_MS.hours).toBe(MS_PER_HOUR));
    it('days = MS_PER_DAY', () => expect(DURATION_UNIT_MS.days).toBe(MS_PER_DAY));
  });

  describe('isWeekendDay', () => {
    it('returns true for Sunday (0)', () => expect(isWeekendDay(SUNDAY)).toBe(true));
    it('returns true for Saturday (6)', () => expect(isWeekendDay(SATURDAY)).toBe(true));
    it('returns false for Monday (1)', () => expect(isWeekendDay(1)).toBe(false));
    it('returns false for Wednesday (3)', () => expect(isWeekendDay(3)).toBe(false));
    it('returns false for Friday (5)', () => expect(isWeekendDay(5)).toBe(false));
  });

  describe('RRULE_DAY_MAP', () => {
    it('maps SU to 0', () => expect(RRULE_DAY_MAP.SU).toBe(0));
    it('maps MO to 1', () => expect(RRULE_DAY_MAP.MO).toBe(1));
    it('maps SA to 6', () => expect(RRULE_DAY_MAP.SA).toBe(6));
    it('has 7 entries', () => expect(Object.keys(RRULE_DAY_MAP)).toHaveLength(7));
  });
});
