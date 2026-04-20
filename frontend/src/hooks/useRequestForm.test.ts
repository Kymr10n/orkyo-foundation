import { describe, it, expect } from 'vitest';
import { formReducer, buildInitialState } from './useRequestForm';
import type { RequestFormState } from './useRequestForm';
import type { Request } from '@/types/requests';
import type { Template } from '@/types/templates';

function makeState(overrides: Partial<RequestFormState> = {}): RequestFormState {
  return {
    name: '',
    description: '',
    planningMode: 'leaf',
    parentRequestId: '',
    selectedSpaceId: '',
    startDate: '',
    startTime: '09:00',
    endDate: '',
    endTime: '17:00',
    earliestStartDate: '',
    earliestStartTime: '',
    latestEndDate: '',
    latestEndTime: '',
    durationValue: 1,
    durationUnit: 'days',
    schedulingSettingsApply: true,
    requirements: new Map(),
    selectedCriterionId: '',
    openSections: { basic: true, schedule: true, constraints: false, duration: true, requirements: true },
    ...overrides,
  };
}

describe('formReducer', () => {
  describe('SET_FIELD', () => {
    it('updates a string field', () => {
      const state = makeState();
      const result = formReducer(state, { type: 'SET_FIELD', field: 'name', value: 'Test' });
      expect(result.name).toBe('Test');
    });

    it('does not mutate original state', () => {
      const state = makeState({ name: 'Original' });
      formReducer(state, { type: 'SET_FIELD', field: 'name', value: 'Changed' });
      expect(state.name).toBe('Original');
    });
  });

  describe('TOGGLE_SECTION', () => {
    it('toggles a section from false to true', () => {
      const state = makeState();
      expect(state.openSections.constraints).toBe(false);
      const result = formReducer(state, { type: 'TOGGLE_SECTION', section: 'constraints' });
      expect(result.openSections.constraints).toBe(true);
    });

    it('toggles a section from true to false', () => {
      const state = makeState();
      expect(state.openSections.basic).toBe(true);
      const result = formReducer(state, { type: 'TOGGLE_SECTION', section: 'basic' });
      expect(result.openSections.basic).toBe(false);
    });

    it('preserves other sections', () => {
      const state = makeState();
      const result = formReducer(state, { type: 'TOGGLE_SECTION', section: 'constraints' });
      expect(result.openSections.basic).toBe(true);
      expect(result.openSections.schedule).toBe(true);
    });
  });

  describe('ADD_REQUIREMENT', () => {
    it('adds a requirement and clears selectedCriterionId', () => {
      const state = makeState({ selectedCriterionId: 'crit-1' });
      const result = formReducer(state, { type: 'ADD_REQUIREMENT', criterionId: 'crit-1', value: true });
      expect(result.requirements.get('crit-1')).toBe(true);
      expect(result.selectedCriterionId).toBe('');
    });

    it('preserves existing requirements', () => {
      const existing = new Map([['crit-1', 'val1']]);
      const state = makeState({ requirements: existing });
      const result = formReducer(state, { type: 'ADD_REQUIREMENT', criterionId: 'crit-2', value: 'val2' });
      expect(result.requirements.size).toBe(2);
      expect(result.requirements.get('crit-1')).toBe('val1');
    });
  });

  describe('REMOVE_REQUIREMENT', () => {
    it('removes a requirement by criterionId', () => {
      const existing = new Map([['crit-1', 'val1'], ['crit-2', 'val2']]);
      const state = makeState({ requirements: existing });
      const result = formReducer(state, { type: 'REMOVE_REQUIREMENT', criterionId: 'crit-1' });
      expect(result.requirements.has('crit-1')).toBe(false);
      expect(result.requirements.get('crit-2')).toBe('val2');
    });

    it('is a no-op for non-existent criterionId', () => {
      const state = makeState();
      const result = formReducer(state, { type: 'REMOVE_REQUIREMENT', criterionId: 'ghost' });
      expect(result.requirements.size).toBe(0);
    });
  });

  describe('UPDATE_REQUIREMENT', () => {
    it('updates an existing requirement value', () => {
      const existing = new Map([['crit-1', 'old']]);
      const state = makeState({ requirements: existing });
      const result = formReducer(state, { type: 'UPDATE_REQUIREMENT', criterionId: 'crit-1', value: 'new' });
      expect(result.requirements.get('crit-1')).toBe('new');
    });
  });

  describe('APPLY_TEMPLATE', () => {
    it('applies template duration and requirements', () => {
      const template: Template = {
        id: 't1',
        name: 'Template 1',
        entityType: 'request',
        durationValue: 4,
        durationUnit: 'hours',
        items: [
          { id: 'i1', criterionId: 'c1', value: 'true', templateId: 't1' },
          { id: 'i2', criterionId: 'c2', value: 'high', templateId: 't1' },
        ],
      };
      const state = makeState({ durationValue: 1, durationUnit: 'days' });
      const result = formReducer(state, { type: 'APPLY_TEMPLATE', template });
      expect(result.durationValue).toBe(4);
      expect(result.durationUnit).toBe('hours');
      expect(result.requirements.size).toBe(2);
      expect(result.requirements.get('c1')).toBe('true');
      expect(result.requirements.get('c2')).toBe('high');
    });

    it('defaults duration when template has no duration', () => {
      const template: Template = {
        id: 't1',
        name: 'Minimal',
        entityType: 'request',
      };
      const state = makeState();
      const result = formReducer(state, { type: 'APPLY_TEMPLATE', template });
      expect(result.durationValue).toBe(1);
      expect(result.durationUnit).toBe('hours');
    });

    it('replaces existing requirements', () => {
      const existing = new Map([['old-crit', 'old-val']]);
      const template: Template = {
        id: 't1',
        name: 'T',
        entityType: 'request',
        items: [{ id: 'i1', criterionId: 'new-crit', value: 'new-val', templateId: 't1' }],
      };
      const state = makeState({ requirements: existing });
      const result = formReducer(state, { type: 'APPLY_TEMPLATE', template });
      expect(result.requirements.has('old-crit')).toBe(false);
      expect(result.requirements.get('new-crit')).toBe('new-val');
    });

    it('preserves schedulingSettingsApply', () => {
      const template: Template = { id: 't1', name: 'T', entityType: 'request' };
      const state = makeState({ schedulingSettingsApply: false });
      const result = formReducer(state, { type: 'APPLY_TEMPLATE', template });
      expect(result.schedulingSettingsApply).toBe(false);
    });
  });

  it('returns state unchanged for unknown action', () => {
    const state = makeState();
    // @ts-expect-error testing unknown action
    const result = formReducer(state, { type: 'UNKNOWN' });
    expect(result).toBe(state);
  });
});

describe('buildInitialState', () => {
  it('returns default state when no arguments', () => {
    const state = buildInitialState();
    expect(state.name).toBe('');
    expect(state.planningMode).toBe('leaf');
    expect(state.durationValue).toBe(1);
    expect(state.durationUnit).toBe('days');
    expect(state.requirements.size).toBe(0);
  });

  it('sets parentRequestId when provided', () => {
    const state = buildInitialState(null, 'parent-123');
    expect(state.parentRequestId).toBe('parent-123');
  });

  it('sets defaultPlanningMode', () => {
    const state = buildInitialState(null, undefined, 'container');
    expect(state.planningMode).toBe('container');
  });

  it('sets both parentRequestId and defaultPlanningMode', () => {
    const state = buildInitialState(null, 'p-1', 'container');
    expect(state.parentRequestId).toBe('p-1');
    expect(state.planningMode).toBe('container');
  });

  it('builds state from an existing Request', () => {
    const request: Request = {
      id: 'r1',
      name: 'Test Request',
      description: 'A description',
      planningMode: 'leaf',
      sortOrder: 0,
      spaceId: 'space-1',
      startTs: '2026-04-17T09:00:00Z',
      endTs: '2026-04-17T17:00:00Z',
      minimalDurationValue: 8,
      minimalDurationUnit: 'hours',
      schedulingSettingsApply: false,
      status: 'planned',
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      requirements: [
        { id: 'rq1', criterionId: 'c1', value: 'true', requestId: 'r1' },
      ],
    };
    const state = buildInitialState(request);
    expect(state.name).toBe('Test Request');
    expect(state.description).toBe('A description');
    expect(state.selectedSpaceId).toBe('space-1');
    expect(state.durationValue).toBe(8);
    expect(state.durationUnit).toBe('hours');
    expect(state.schedulingSettingsApply).toBe(false);
    expect(state.requirements.get('c1')).toBe('true');
    expect(state.startDate).not.toBe('');
  });

  it('handles Request without optional fields', () => {
    const request: Request = {
      id: 'r1',
      name: 'Minimal',
      planningMode: 'leaf',
      sortOrder: 0,
      minimalDurationValue: 1,
      minimalDurationUnit: 'days',
      schedulingSettingsApply: true,
      status: 'planned',
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    };
    const state = buildInitialState(request);
    expect(state.name).toBe('Minimal');
    expect(state.description).toBe('');
    expect(state.selectedSpaceId).toBe('');
    expect(state.startDate).toBe('');
    expect(state.requirements.size).toBe(0);
  });
});
