import { describe, it, expect } from 'vitest';
import {
  DEFAULT_START_TIME,
  DEFAULT_END_TIME,
  DEFAULT_DURATION_VALUE,
  DEFAULT_DURATION_UNIT,
  SPACE_NONE_PLACEHOLDER,
  VALIDATION_MESSAGES,
} from '@foundation/src/constants';

describe('Constants', () => {
  describe('Time defaults', () => {
    it('should have valid start time format', () => {
      expect(DEFAULT_START_TIME).toMatch(/^\d{2}:\d{2}$/);
    });

    it('should have valid end time format', () => {
      expect(DEFAULT_END_TIME).toMatch(/^\d{2}:\d{2}$/);
    });

    it('should have start time before end time', () => {
      const [startHour, startMin] = DEFAULT_START_TIME.split(':').map(Number);
      const [endHour, endMin] = DEFAULT_END_TIME.split(':').map(Number);
      const startMinutes = startHour * 60 + startMin;
      const endMinutes = endHour * 60 + endMin;
      expect(startMinutes).toBeLessThan(endMinutes);
    });
  });

  describe('Duration defaults', () => {
    it('should have positive duration value', () => {
      expect(DEFAULT_DURATION_VALUE).toBeGreaterThan(0);
    });

    it('should have valid duration unit', () => {
      const validUnits = ['minutes', 'hours', 'days', 'weeks', 'months', 'years'];
      expect(validUnits).toContain(DEFAULT_DURATION_UNIT);
    });
  });

  describe('Form placeholders', () => {
    it('should have space none placeholder', () => {
      expect(SPACE_NONE_PLACEHOLDER).toBe('__none__');
      expect(SPACE_NONE_PLACEHOLDER).not.toBe('');
    });

    it('should be a string that cannot be a valid UUID', () => {
      const uuidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
      expect(SPACE_NONE_PLACEHOLDER).not.toMatch(uuidPattern);
    });
  });

  describe('Validation messages', () => {
    it('should have all required validation messages', () => {
      expect(VALIDATION_MESSAGES.REQUEST_NAME_REQUIRED).toBeTruthy();
      expect(VALIDATION_MESSAGES.END_BEFORE_START).toBeTruthy();
      expect(VALIDATION_MESSAGES.DATES_MUST_BE_TOGETHER).toBeTruthy();
      expect(VALIDATION_MESSAGES.CONSTRAINT_ORDER).toBeTruthy();
      expect(VALIDATION_MESSAGES.START_BEFORE_CONSTRAINT).toBeTruthy();
      expect(VALIDATION_MESSAGES.END_AFTER_CONSTRAINT).toBeTruthy();
      expect(VALIDATION_MESSAGES.SAVE_FAILED).toBeTruthy();
    });

    it('should have non-empty messages', () => {
      Object.values(VALIDATION_MESSAGES).forEach(message => {
        expect(message.length).toBeGreaterThan(0);
      });
    });

    it('should have descriptive messages', () => {
      Object.values(VALIDATION_MESSAGES).forEach(message => {
        expect(message.length).toBeGreaterThan(10);
      });
    });

    it('should be defined as const for type safety', () => {
      // TypeScript enforces const at compile time, but runtime mutations are still possible in JS
      // This test just verifies the object exists and is properly typed
      expect(typeof VALIDATION_MESSAGES).toBe('object');
      expect(VALIDATION_MESSAGES.REQUEST_NAME_REQUIRED).toBeTruthy();
    });
  });
});
