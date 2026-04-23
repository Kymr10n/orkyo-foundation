import { describe, it, expect } from 'vitest';
import { isModified, formatRange, isColorSetting, validate } from './tenant-config-helpers';
import type { TenantSettingDescriptor } from '@foundation/src/lib/api/tenant-settings-api';

function makeDescriptor(overrides: Partial<TenantSettingDescriptor> = {}): TenantSettingDescriptor {
  return {
    key: 'test.key',
    category: 'security',
    label: 'Test Setting',
    description: 'A test setting',
    valueType: 'string',
    currentValue: 'hello',
    defaultValue: 'hello',
    minValue: null,
    maxValue: null,
    enumValues: null,
    ...overrides,
  } as TenantSettingDescriptor;
}

describe('isModified', () => {
  it('returns false when currentValue equals defaultValue', () => {
    expect(isModified(makeDescriptor())).toBe(false);
  });

  it('returns true when currentValue differs from defaultValue', () => {
    expect(isModified(makeDescriptor({ currentValue: 'world' }))).toBe(true);
  });
});

describe('formatRange', () => {
  it('returns "min – max" when both are set', () => {
    expect(formatRange(makeDescriptor({ minValue: '1', maxValue: '100' }))).toBe('1 – 100');
  });

  it('returns "≥ min" when only minValue is set', () => {
    expect(formatRange(makeDescriptor({ minValue: '5' }))).toBe('≥ 5');
  });

  it('returns "≤ max" when only maxValue is set', () => {
    expect(formatRange(makeDescriptor({ maxValue: '50' }))).toBe('≤ 50');
  });

  it('returns null when neither is set', () => {
    expect(formatRange(makeDescriptor())).toBeNull();
  });
});

describe('isColorSetting', () => {
  it('returns true for string type with _color suffix', () => {
    expect(isColorSetting(makeDescriptor({ valueType: 'string', key: 'branding.primary_color' }))).toBe(true);
  });

  it('returns false for non-color key', () => {
    expect(isColorSetting(makeDescriptor({ valueType: 'string', key: 'branding.name' }))).toBe(false);
  });

  it('returns false for non-string type even with _color suffix', () => {
    expect(isColorSetting(makeDescriptor({ valueType: 'int', key: 'test_color' }))).toBe(false);
  });
});

describe('validate', () => {
  describe('int type', () => {
    const intDescriptor = makeDescriptor({ valueType: 'int', minValue: '1', maxValue: '100' });

    it('returns null for valid integer', () => {
      expect(validate(intDescriptor, '50')).toBeNull();
    });

    it('rejects non-integer', () => {
      expect(validate(intDescriptor, 'abc')).toBe('Must be a whole number');
    });

    it('rejects decimal', () => {
      expect(validate(intDescriptor, '3.14')).toBe('Must be a whole number');
    });

    it('rejects value below minimum', () => {
      expect(validate(intDescriptor, '0')).toBe('Minimum is 1');
    });

    it('rejects value above maximum', () => {
      expect(validate(intDescriptor, '101')).toBe('Maximum is 100');
    });

    it('accepts negative integers when allowed', () => {
      const d = makeDescriptor({ valueType: 'int', minValue: '-10', maxValue: '10' });
      expect(validate(d, '-5')).toBeNull();
    });
  });

  describe('double type', () => {
    const doubleDescriptor = makeDescriptor({ valueType: 'double', minValue: '0.1', maxValue: '99.9' });

    it('returns null for valid double', () => {
      expect(validate(doubleDescriptor, '3.14')).toBeNull();
    });

    it('rejects non-number', () => {
      expect(validate(doubleDescriptor, 'abc')).toBe('Must be a number');
    });

    it('rejects value below minimum', () => {
      expect(validate(doubleDescriptor, '0.05')).toBe('Minimum is 0.1');
    });

    it('rejects value above maximum', () => {
      expect(validate(doubleDescriptor, '100')).toBe('Maximum is 99.9');
    });
  });

  describe('string type', () => {
    it('rejects empty string', () => {
      expect(validate(makeDescriptor({ valueType: 'string' }), '   ')).toBe('Cannot be empty');
    });

    it('accepts non-empty string', () => {
      expect(validate(makeDescriptor({ valueType: 'string' }), 'hello')).toBeNull();
    });
  });
});
