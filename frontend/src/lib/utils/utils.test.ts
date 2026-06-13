import { describe, it, expect } from 'vitest';
import {
  buildCreatePayload,
  buildUpdatePayload,
  cn,
  combineDateTimeToISO,
  durationToMinutes,
  formatDateDisplay,
  formatDateForInput,
  formatDuration,
  formatMinutesHuman,
  formatStatusLabel,
  formatTimeForInput,
  getDataTypeColor,
  getStatusColor,
  getStatusDotColor,
  isValidSlug,
} from './utils';
import type { RequestFormData } from '@foundation/src/components/requests/RequestFormDialog';

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

describe('Request payload builders — icon plumbing', () => {
  const base: RequestFormData = {
    name: 'test',
    description: 'desc',
    planningMode: 'leaf',
    duration: { value: 1, unit: 'hours' },
    schedulingSettingsApply: true,
    requirements: [],
  };

  it('buildCreatePayload forwards a chosen icon', () => {
    const out = buildCreatePayload({ ...base, icon: 'calendar' });
    expect(out.icon).toBe('calendar');
  });

  it('buildCreatePayload forwards null when no icon is chosen', () => {
    const out = buildCreatePayload({ ...base, icon: null });
    expect(out.icon).toBeNull();
  });

  it('buildCreatePayload omits icon when it is not on the form data at all', () => {
    const out = buildCreatePayload(base);
    expect(out.icon).toBeUndefined();
  });

  it('payload builders drop requirements with a null value and keep the rest', () => {
    const withReqs: RequestFormData = {
      ...base,
      requirements: [
        { criterionId: 'c1', value: 'x', operator: 'eq' },
        { criterionId: 'c2', value: null },
        { criterionId: 'c3', value: 42 },
      ],
    };
    for (const out of [buildCreatePayload(withReqs), buildUpdatePayload(withReqs)]) {
      expect(out.requirements).toEqual([
        { criterionId: 'c1', value: 'x', operator: 'eq' },
        { criterionId: 'c3', value: 42 },
      ]);
    }
  });

  it('buildUpdatePayload forwards a chosen icon', () => {
    const out = buildUpdatePayload({ ...base, icon: 'hammer' });
    expect(out.icon).toBe('hammer');
  });

  it('buildUpdatePayload forwards null when the form cleared the icon', () => {
    const out = buildUpdatePayload({ ...base, icon: null });
    expect(out.icon).toBeNull();
  });

  it('buildUpdatePayload omits planningMode when it is unchanged from the original', () => {
    const out = buildUpdatePayload({ ...base, planningMode: 'summary' }, 'summary');
    expect(out.planningMode).toBeUndefined();
  });

  it('buildUpdatePayload sends planningMode when the user changed the type', () => {
    const out = buildUpdatePayload({ ...base, planningMode: 'leaf' }, 'summary');
    expect(out.planningMode).toBe('leaf');
  });

  it('buildUpdatePayload sends planningMode when no original is provided', () => {
    const out = buildUpdatePayload({ ...base, planningMode: 'leaf' });
    expect(out.planningMode).toBe('leaf');
  });

  it('buildCreatePayload forwards the chosen siteId', () => {
    const out = buildCreatePayload({ ...base, siteId: 'site-A' });
    expect(out.siteId).toBe('site-A');
  });

  it('buildCreatePayload omits siteId when site-neutral', () => {
    const out = buildCreatePayload({ ...base, siteId: null });
    expect(out.siteId).toBeUndefined();
  });

  it('buildUpdatePayload omits siteId when unchanged from the original', () => {
    const out = buildUpdatePayload({ ...base, siteId: 'site-A' }, undefined, 'site-A');
    expect(out.siteId).toBeUndefined();
  });

  it('buildUpdatePayload sends siteId when the user re-scoped', () => {
    const out = buildUpdatePayload({ ...base, siteId: 'site-B' }, undefined, 'site-A');
    expect(out.siteId).toBe('site-B');
  });

  it('buildUpdatePayload omits siteId when no original is provided (safe default)', () => {
    const out = buildUpdatePayload({ ...base, siteId: 'site-A' });
    expect(out.siteId).toBeUndefined();
  });
});

describe('cn', () => {
  it('merges class names and dedupes conflicting tailwind utilities', () => {
    expect(cn('px-2', 'px-4')).toBe('px-4');
    expect(cn('text-sm', false, 'font-bold')).toBe('text-sm font-bold');
  });
});

describe('isValidSlug', () => {
  it('accepts alphanumerics, underscores and hyphens', () => {
    expect(isValidSlug('site_01-A')).toBe(true);
  });
  it('rejects spaces and other punctuation', () => {
    expect(isValidSlug('has space')).toBe(false);
    expect(isValidSlug('with.dot')).toBe(false);
    expect(isValidSlug('')).toBe(false);
  });
});

describe('formatDuration', () => {
  it('singularizes the unit when value is 1', () => {
    expect(formatDuration(1, 'days')).toBe('1 day');
    expect(formatDuration(1, 'hours')).toBe('1 hour');
  });
  it('keeps the plural unit otherwise', () => {
    expect(formatDuration(2, 'hours')).toBe('2 hours');
    expect(formatDuration(0, 'minutes')).toBe('0 minutes');
  });
});

describe('formatDateDisplay', () => {
  it('returns a dash for null/undefined/empty input', () => {
    expect(formatDateDisplay(null)).toBe('-');
    expect(formatDateDisplay(undefined)).toBe('-');
    expect(formatDateDisplay('')).toBe('-');
  });
  it('renders a locale date for a valid ISO string', () => {
    expect(formatDateDisplay('2026-04-02T10:30:00Z')).toBe(
      new Date('2026-04-02T10:30:00Z').toLocaleDateString(),
    );
  });
});

describe('status helpers', () => {
  it('getStatusColor covers every known status plus the default', () => {
    expect(getStatusColor('planned')).toContain('blue');
    expect(getStatusColor('in_progress')).toContain('yellow');
    expect(getStatusColor('done')).toContain('green');
    expect(getStatusColor('cancelled')).toContain('line-through');
    expect(getStatusColor('???')).toBe('bg-muted text-muted-foreground');
  });
  it('getStatusDotColor covers every known status plus the default', () => {
    expect(getStatusDotColor('planned')).toBe('bg-blue-500');
    expect(getStatusDotColor('in_progress')).toBe('bg-yellow-500');
    expect(getStatusDotColor('done')).toBe('bg-green-500');
    expect(getStatusDotColor('cancelled')).toBe('bg-gray-400');
    expect(getStatusDotColor('???')).toBe('bg-gray-400');
  });
  it('formatStatusLabel humanizes known statuses and echoes unknown ones', () => {
    expect(formatStatusLabel('planned')).toBe('Planned');
    expect(formatStatusLabel('in_progress')).toBe('In Progress');
    expect(formatStatusLabel('done')).toBe('Done');
    expect(formatStatusLabel('cancelled')).toBe('Cancelled');
    expect(formatStatusLabel('custom')).toBe('custom');
  });
});

describe('durationToMinutes', () => {
  it('minutes pass through', () => expect(durationToMinutes(30, 'minutes')).toBe(30));
  it('hours → minutes',      () => expect(durationToMinutes(2,  'hours')).toBe(120));
  it('days → minutes',       () => expect(durationToMinutes(1,  'days')).toBe(1440));
  it('weeks → minutes',      () => expect(durationToMinutes(1,  'weeks')).toBe(10080));
  it('months → minutes',     () => expect(durationToMinutes(1,  'months')).toBe(43200));
  it('years → minutes',      () => expect(durationToMinutes(1,  'years')).toBe(525600));
});

describe('formatMinutesHuman', () => {
  it('formats minutes-only durations', () => {
    expect(formatMinutesHuman(5)).toBe('5m');
    expect(formatMinutesHuman(59)).toBe('59m');
  });
  it('formats hours, with and without trailing minutes', () => {
    expect(formatMinutesHuman(60)).toBe('1h');
    expect(formatMinutesHuman(90)).toBe('1h 30m');
  });
  it('formats days, with and without trailing hours', () => {
    expect(formatMinutesHuman(1440)).toBe('1d');
    expect(formatMinutesHuman(1440 + 120)).toBe('1d 2h');
  });
  it('formats weeks, with and without trailing days', () => {
    expect(formatMinutesHuman(10080)).toBe('1w');
    expect(formatMinutesHuman(10080 + 1440)).toBe('1w 1d');
  });
});
