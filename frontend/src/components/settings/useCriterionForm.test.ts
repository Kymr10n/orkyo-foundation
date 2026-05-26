import { renderHook, act } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { useCriterionForm, useSeedCriterionForm } from './useCriterionForm';
import type { ResourceTypeKey } from '@foundation/src/types/criterion';

describe('useCriterionForm', () => {
  describe('initial state', () => {
    it('defaults to empty when no initial value provided', () => {
      const { result } = renderHook(() => useCriterionForm());
      expect(result.current.description).toBe('');
      expect(result.current.unit).toBe('');
      expect(result.current.enumValues).toEqual([]);
      expect(result.current.resourceTypeKeys).toEqual([]);
    });

    it('seeds from provided initial value', () => {
      const { result } = renderHook(() =>
        useCriterionForm({ description: 'Capacity', unit: 'seats', resourceTypeKeys: ['space'] }),
      );
      expect(result.current.description).toBe('Capacity');
      expect(result.current.unit).toBe('seats');
      expect(result.current.resourceTypeKeys).toEqual(['space']);
    });
  });

  describe('toggleResourceType', () => {
    it('adds a key when checked is true', () => {
      const { result } = renderHook(() => useCriterionForm());
      act(() => result.current.toggleResourceType('space', true));
      expect(result.current.resourceTypeKeys).toEqual(['space']);
    });

    it('removes a key when checked is false', () => {
      const { result } = renderHook(() => useCriterionForm({ resourceTypeKeys: ['space', 'person'] }));
      act(() => result.current.toggleResourceType('space', false));
      expect(result.current.resourceTypeKeys).toEqual(['person']);
    });

    it('does not add duplicates', () => {
      const { result } = renderHook(() => useCriterionForm({ resourceTypeKeys: ['space'] }));
      act(() => result.current.toggleResourceType('space', true));
      expect(result.current.resourceTypeKeys).toEqual(['space', 'space']);
    });
  });

  describe('validate', () => {
    it('returns null for a valid Numeric criterion', () => {
      const { result } = renderHook(() =>
        useCriterionForm({ resourceTypeKeys: ['space'] }),
      );
      expect(result.current.validate('Number')).toBeNull();
    });

    it('returns null for a valid Boolean criterion', () => {
      const { result } = renderHook(() =>
        useCriterionForm({ resourceTypeKeys: ['person'] }),
      );
      expect(result.current.validate('Boolean')).toBeNull();
    });

    it('returns error when Enum has no values', () => {
      const { result } = renderHook(() =>
        useCriterionForm({ resourceTypeKeys: ['space'], enumValues: [] }),
      );
      expect(result.current.validate('Enum')).toBe('At least one enum value is required');
    });

    it('returns null for a valid Enum criterion with values', () => {
      const { result } = renderHook(() =>
        useCriterionForm({ resourceTypeKeys: ['space'], enumValues: ['A', 'B'] }),
      );
      expect(result.current.validate('Enum')).toBeNull();
    });

    it('returns error when no resource type selected', () => {
      const { result } = renderHook(() => useCriterionForm({ resourceTypeKeys: [] }));
      expect(result.current.validate('Number')).toBe('At least one applicability scope must be selected');
    });

    it('enum check takes priority over resource type check', () => {
      const { result } = renderHook(() =>
        useCriterionForm({ resourceTypeKeys: [], enumValues: [] }),
      );
      expect(result.current.validate('Enum')).toBe('At least one enum value is required');
    });
  });

  describe('reset', () => {
    it('resets to empty when called with no argument', () => {
      const { result } = renderHook(() =>
        useCriterionForm({ description: 'Old', unit: 'kg', resourceTypeKeys: ['space'] }),
      );
      act(() => result.current.reset());
      expect(result.current.description).toBe('');
      expect(result.current.unit).toBe('');
      expect(result.current.resourceTypeKeys).toEqual([]);
    });

    it('resets to provided partial state', () => {
      const { result } = renderHook(() =>
        useCriterionForm({ description: 'Old', unit: 'kg' }),
      );
      act(() => result.current.reset({ description: 'New', resourceTypeKeys: ['tool'] }));
      expect(result.current.description).toBe('New');
      expect(result.current.unit).toBe('');
      expect(result.current.resourceTypeKeys).toEqual(['tool']);
    });
  });
});

describe('useSeedCriterionForm', () => {
  it('seeds form when source is provided', () => {
    const source = { description: 'Load', unit: 'kg', enumValues: [], resourceTypeKeys: ['space'] as ResourceTypeKey[] };
    const { result } = renderHook(() => {
      const form = useCriterionForm();
      useSeedCriterionForm(form, source);
      return form;
    });
    expect(result.current.description).toBe('Load');
    expect(result.current.unit).toBe('kg');
    expect(result.current.resourceTypeKeys).toEqual(['space']);
  });

  it('does not crash when source is undefined', () => {
    expect(() =>
      renderHook(() => {
        const form = useCriterionForm();
        useSeedCriterionForm(form, undefined);
        return form;
      }),
    ).not.toThrow();
  });

  it('re-seeds when source identity changes', () => {
    let source = { description: 'First', unit: '', enumValues: [], resourceTypeKeys: [] as ResourceTypeKey[] };
    const { result, rerender } = renderHook(() => {
      const form = useCriterionForm();
      useSeedCriterionForm(form, source);
      return form;
    });
    expect(result.current.description).toBe('First');

    source = { description: 'Second', unit: '', enumValues: [], resourceTypeKeys: [] as ResourceTypeKey[] };
    rerender();
    expect(result.current.description).toBe('Second');
  });
});
