import { describe, it, expect } from 'vitest';
import { templateFormReducer, getDefaultValueForCriterion } from './useTemplateForm';
import type { TemplateFormState } from './useTemplateForm';
import type { Criterion } from '@/types/criterion';
import type { Template } from '@/types/templates';

function makeState(overrides: Partial<TemplateFormState> = {}): TemplateFormState {
  return {
    name: '',
    description: '',
    durationValue: '1',
    durationUnit: 'days',
    requirements: new Map(),
    ...overrides,
  };
}

function makeCriterion(overrides: Partial<Criterion> = {}): Criterion {
  return {
    id: 'c1',
    name: 'Test Criterion',
    dataType: 'String',
    createdAt: '2026-01-01',
    updatedAt: '2026-01-01',
    ...overrides,
  };
}

describe('getDefaultValueForCriterion', () => {
  it('returns false for Boolean criteria', () => {
    expect(getDefaultValueForCriterion(makeCriterion({ dataType: 'Boolean' }))).toBe(false);
  });

  it('returns null for Text criteria', () => {
    expect(getDefaultValueForCriterion(makeCriterion({ dataType: 'String' }))).toBeNull();
  });

  it('returns null for Number criteria', () => {
    expect(getDefaultValueForCriterion(makeCriterion({ dataType: 'Number' }))).toBeNull();
  });

  it('returns null for Enum criteria', () => {
    expect(getDefaultValueForCriterion(makeCriterion({ dataType: 'Enum' }))).toBeNull();
  });
});

describe('templateFormReducer', () => {
  describe('SET_FIELD', () => {
    it('updates a field value', () => {
      const state = makeState();
      const result = templateFormReducer(state, { type: 'SET_FIELD', field: 'name', value: 'New Name' });
      expect(result.name).toBe('New Name');
    });

    it('does not mutate original state', () => {
      const state = makeState({ name: 'Original' });
      templateFormReducer(state, { type: 'SET_FIELD', field: 'name', value: 'Changed' });
      expect(state.name).toBe('Original');
    });
  });

  describe('ADD_REQUIREMENT', () => {
    it('adds a requirement with default value for Boolean criterion', () => {
      const state = makeState();
      const criterion = makeCriterion({ id: 'bool-c', dataType: 'Boolean' });
      const result = templateFormReducer(state, { type: 'ADD_REQUIREMENT', criterionId: 'bool-c', criterion });
      expect(result.requirements.get('bool-c')).toBe(false);
    });

    it('adds a requirement with null for non-Boolean criterion', () => {
      const state = makeState();
      const criterion = makeCriterion({ id: 'text-c', dataType: 'String' });
      const result = templateFormReducer(state, { type: 'ADD_REQUIREMENT', criterionId: 'text-c', criterion });
      expect(result.requirements.get('text-c')).toBeNull();
    });

    it('preserves existing requirements', () => {
      const existing = new Map([['c1', 'val']]);
      const state = makeState({ requirements: existing });
      const criterion = makeCriterion({ id: 'c2' });
      const result = templateFormReducer(state, { type: 'ADD_REQUIREMENT', criterionId: 'c2', criterion });
      expect(result.requirements.size).toBe(2);
      expect(result.requirements.get('c1')).toBe('val');
    });
  });

  describe('REMOVE_REQUIREMENT', () => {
    it('removes a requirement', () => {
      const existing = new Map([['c1', 'val1'], ['c2', 'val2']]);
      const state = makeState({ requirements: existing });
      const result = templateFormReducer(state, { type: 'REMOVE_REQUIREMENT', criterionId: 'c1' });
      expect(result.requirements.has('c1')).toBe(false);
      expect(result.requirements.get('c2')).toBe('val2');
    });
  });

  describe('UPDATE_REQUIREMENT', () => {
    it('updates a requirement value', () => {
      const existing = new Map([['c1', 'old']]);
      const state = makeState({ requirements: existing });
      const result = templateFormReducer(state, { type: 'UPDATE_REQUIREMENT', criterionId: 'c1', value: 'new' });
      expect(result.requirements.get('c1')).toBe('new');
    });
  });

  describe('LOAD_TEMPLATE', () => {
    it('loads all template fields', () => {
      const template: Template = {
        id: 't1',
        name: 'My Template',
        description: 'Template desc',
        entityType: 'request',
        durationValue: 4,
        durationUnit: 'hours',
        items: [
          { id: 'i1', criterionId: 'c1', value: 'true', templateId: 't1' },
          { id: 'i2', criterionId: 'c2', value: 'medium', templateId: 't1' },
        ],
      };
      const state = makeState();
      const result = templateFormReducer(state, { type: 'LOAD_TEMPLATE', template });
      expect(result.name).toBe('My Template');
      expect(result.description).toBe('Template desc');
      expect(result.durationValue).toBe('4');
      expect(result.durationUnit).toBe('hours');
      expect(result.requirements.size).toBe(2);
      expect(result.requirements.get('c1')).toBe('true');
    });

    it('handles template without optional fields', () => {
      const template: Template = { id: 't1', name: 'Minimal', entityType: 'request' };
      const state = makeState({ name: 'Old', durationValue: '10' });
      const result = templateFormReducer(state, { type: 'LOAD_TEMPLATE', template });
      expect(result.name).toBe('Minimal');
      expect(result.description).toBe('');
      expect(result.durationValue).toBe('1');
      expect(result.durationUnit).toBe('hours');
      expect(result.requirements.size).toBe(0);
    });
  });

  describe('RESET', () => {
    it('resets to empty state', () => {
      const state = makeState({
        name: 'Something',
        description: 'Desc',
        durationValue: '10',
        requirements: new Map([['c1', 'v']]),
      });
      const result = templateFormReducer(state, { type: 'RESET' });
      expect(result.name).toBe('');
      expect(result.description).toBe('');
      expect(result.durationValue).toBe('1');
      expect(result.durationUnit).toBe('days');
      expect(result.requirements.size).toBe(0);
    });
  });

  it('returns state unchanged for unknown action', () => {
    const state = makeState();
    // @ts-expect-error testing unknown action
    const result = templateFormReducer(state, { type: 'UNKNOWN' });
    expect(result).toBe(state);
  });
});
