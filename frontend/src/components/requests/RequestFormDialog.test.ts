import { describe, it, expect } from 'vitest';
import { combineDateTimeToISO } from '@foundation/src/lib/utils';
import { VALIDATION_MESSAGES } from '@foundation/src/constants';
import { computeSiteScopeWarning } from './RequestFormDialog';

describe('RequestFormDialog - Validation Logic', () => {
  describe('Date/Time validation', () => {
    it('should detect when end is before start', () => {
      const startTs = combineDateTimeToISO('2026-01-29', '14:00');
      const endTs = combineDateTimeToISO('2026-01-29', '12:00');

      const isValid = new Date(startTs) < new Date(endTs);
      expect(isValid).toBe(false);
    });

    it('should allow end after start', () => {
      const startTs = combineDateTimeToISO('2026-01-29', '12:00');
      const endTs = combineDateTimeToISO('2026-01-29', '14:00');

      const isValid = new Date(startTs) < new Date(endTs);
      expect(isValid).toBe(true);
    });

    it('should detect when end equals start', () => {
      const startTs = combineDateTimeToISO('2026-01-29', '12:00');
      const endTs = combineDateTimeToISO('2026-01-29', '12:00');

      const isInvalid = new Date(startTs) >= new Date(endTs);
      expect(isInvalid).toBe(true);
    });

    it('should handle multi-day events', () => {
      const startTs = combineDateTimeToISO('2026-01-29', '14:00');
      const endTs = combineDateTimeToISO('2026-01-30', '10:00');

      const isValid = new Date(startTs) < new Date(endTs);
      expect(isValid).toBe(true);
    });
  });

  describe('Constraint validation', () => {
    it('should detect when earliest is after latest', () => {
      const earliestStartTs = combineDateTimeToISO('2026-01-30', '09:00');
      const latestEndTs = combineDateTimeToISO('2026-01-29', '17:00');

      const isInvalid = new Date(earliestStartTs) >= new Date(latestEndTs);
      expect(isInvalid).toBe(true);
    });

    it('should allow earliest before latest', () => {
      const earliestStartTs = combineDateTimeToISO('2026-01-29', '09:00');
      const latestEndTs = combineDateTimeToISO('2026-01-30', '17:00');

      const isValid = new Date(earliestStartTs) < new Date(latestEndTs);
      expect(isValid).toBe(true);
    });

    it('should detect when start is before earliest constraint', () => {
      const earliestStartTs = combineDateTimeToISO('2026-01-29', '09:00');
      const startTs = combineDateTimeToISO('2026-01-29', '08:00');

      const isInvalid = new Date(startTs) < new Date(earliestStartTs);
      expect(isInvalid).toBe(true);
    });

    it('should detect when end is after latest constraint', () => {
      const latestEndTs = combineDateTimeToISO('2026-01-29', '17:00');
      const endTs = combineDateTimeToISO('2026-01-29', '18:00');

      const isInvalid = new Date(endTs) > new Date(latestEndTs);
      expect(isInvalid).toBe(true);
    });

    it('should allow scheduled dates within constraints', () => {
      const earliestStartTs = combineDateTimeToISO('2026-01-29', '08:00');
      const startTs = combineDateTimeToISO('2026-01-29', '09:00');
      const endTs = combineDateTimeToISO('2026-01-29', '17:00');
      const latestEndTs = combineDateTimeToISO('2026-01-29', '18:00');

      const isValid =
        new Date(startTs) >= new Date(earliestStartTs) &&
        new Date(endTs) <= new Date(latestEndTs) &&
        new Date(startTs) < new Date(endTs);

      expect(isValid).toBe(true);
    });
  });

  describe('Validation messages', () => {
    it('should have message for name required', () => {
      expect(VALIDATION_MESSAGES.REQUEST_NAME_REQUIRED).toBe('Request name is required');
    });

    it('should have message for end before start', () => {
      expect(VALIDATION_MESSAGES.END_BEFORE_START).toBe('End date/time must be after start date/time');
    });

    it('should have message for incomplete dates', () => {
      expect(VALIDATION_MESSAGES.DATES_MUST_BE_TOGETHER).toContain('Both start and end dates');
    });

    it('should have message for constraint order', () => {
      expect(VALIDATION_MESSAGES.CONSTRAINT_ORDER).toBe('Earliest start must be before latest end');
    });

    it('should have message for start before constraint', () => {
      expect(VALIDATION_MESSAGES.START_BEFORE_CONSTRAINT).toContain('earliest start constraint');
    });

    it('should have message for end after constraint', () => {
      expect(VALIDATION_MESSAGES.END_AFTER_CONSTRAINT).toContain('latest end constraint');
    });
  });

  describe('Form state validation', () => {
    it('should require both dates or neither', () => {
      const hasStartOnly = { startDate: '2026-01-29', startTime: '09:00', endDate: '', endTime: '' };
      const hasEndOnly = { startDate: '', startTime: '', endDate: '2026-01-29', endTime: '17:00' };
      const hasBoth = { startDate: '2026-01-29', startTime: '09:00', endDate: '2026-01-29', endTime: '17:00' };
      const hasNeither = { startDate: '', startTime: '', endDate: '', endTime: '' };

      const validatePair = (data: typeof hasStartOnly) => {
        const hasStart = !!(data.startDate && data.startTime);
        const hasEnd = !!(data.endDate && data.endTime);
        return (hasStart && hasEnd) || (!hasStart && !hasEnd);
      };

      expect(validatePair(hasStartOnly)).toBe(false);
      expect(validatePair(hasEndOnly)).toBe(false);
      expect(validatePair(hasBoth)).toBe(true);
      expect(validatePair(hasNeither)).toBe(true);
    });

    it('should validate name is not empty or whitespace', () => {
      const emptyName = '';
      const whitespaceName = '   ';
      const validName = 'My Request';

      expect(emptyName.trim()).toBe('');
      expect(whitespaceName.trim()).toBe('');
      expect(validName.trim()).toBe('My Request');
    });
  });

  describe('Edge cases', () => {
    it('should handle timezone correctly', () => {
      const ts = combineDateTimeToISO('2026-01-29', '12:00');
      expect(ts).toMatch(/Z$/); // Should end with Z (UTC)
    });

    it('should handle leap year dates', () => {
      const leapDay = combineDateTimeToISO('2024-02-29', '12:00');
      const date = new Date(leapDay);
      expect(date.getMonth()).toBe(1); // February (0-indexed)
      expect(date.getDate()).toBe(29);
    });

    it('should handle year boundaries', () => {
      const newYearsEve = combineDateTimeToISO('2025-12-31', '23:59');
      const newYearsDay = combineDateTimeToISO('2026-01-01', '00:01');

      expect(new Date(newYearsEve) < new Date(newYearsDay)).toBe(true);
    });
  });
});

describe('computeSiteScopeWarning', () => {
  it('returns null for single-site tenants (no site scoping shown)', () => {
    expect(computeSiteScopeWarning(false, 'site-1', '', 'Fab')).toBeNull();
  });

  it('returns null when there is no schedule-site context', () => {
    expect(computeSiteScopeWarning(true, null, 'site-2', undefined)).toBeNull();
    expect(computeSiteScopeWarning(true, undefined, '', undefined)).toBeNull();
  });

  it('returns null when the chosen site already matches the schedule site', () => {
    expect(computeSiteScopeWarning(true, 'site-1', 'site-1', 'Fab')).toBeNull();
  });

  it('warns when the request is scoped to a different site', () => {
    const msg = computeSiteScopeWarning(true, 'site-1', 'site-2', 'Fabrication');
    expect(msg).toMatch(/scoped to another site/i);
    expect(msg).toContain("Fabrication's schedule");
  });

  it('warns when the request is site-neutral ("Any site")', () => {
    const msg = computeSiteScopeWarning(true, 'site-1', '', 'Fabrication');
    expect(msg).toMatch(/Any site/i);
    expect(msg).toContain("Fabrication's schedule");
  });

  it('falls back to "this site" when the schedule site name is unknown', () => {
    const msg = computeSiteScopeWarning(true, 'site-1', '', undefined);
    expect(msg).toContain("this site's schedule");
  });
});
