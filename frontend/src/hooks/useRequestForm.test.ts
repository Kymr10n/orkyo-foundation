/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect } from 'vitest';
import { formReducer, buildInitialState } from './useRequestForm';
import type { RequestFormState, RequirementEntry } from './useRequestForm';
import type { Request } from '@foundation/src/types/requests';
import type { Template } from '@foundation/src/types/templates';
import { spaceAssignment } from '@foundation/src/test-utils/request-fixtures';

function makeState(overrides: Partial<RequestFormState> = {}): RequestFormState {
  return {
    name: '',
    description: '',
    icon: null,
    planningMode: 'leaf',
    parentRequestId: '',
    siteId: '',
    selectedResourceId: '',
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

    it('sets icon to a string id', () => {
      const state = makeState();
      const result = formReducer(state, { type: 'SET_FIELD', field: 'icon', value: 'calendar' });
      expect(result.icon).toBe('calendar');
    });

    it('clears icon back to null', () => {
      const state = makeState({ icon: 'calendar' });
      const result = formReducer(state, { type: 'SET_FIELD', field: 'icon', value: null });
      expect(result.icon).toBeNull();
    });
  });

  describe('ADD_REQUIREMENT', () => {
    it('adds a requirement and clears selectedCriterionId', () => {
      const state = makeState({ selectedCriterionId: 'crit-1' });
      const result = formReducer(state, { type: 'ADD_REQUIREMENT', criterionId: 'crit-1', value: true });
      expect(result.requirements.get('crit-1')).toEqual({ value: true });
      expect(result.selectedCriterionId).toBe('');
    });

    it('preserves existing requirements', () => {
      const existing = new Map<string, RequirementEntry>([['crit-1', { value: 'val1' }]]);
      const state = makeState({ requirements: existing });
      const result = formReducer(state, { type: 'ADD_REQUIREMENT', criterionId: 'crit-2', value: 'val2' });
      expect(result.requirements.size).toBe(2);
      expect(result.requirements.get('crit-1')).toEqual({ value: 'val1' });
    });
  });

  describe('REMOVE_REQUIREMENT', () => {
    it('removes a requirement by criterionId', () => {
      const existing = new Map<string, RequirementEntry>([
        ['crit-1', { value: 'val1' }],
        ['crit-2', { value: 'val2' }],
      ]);
      const state = makeState({ requirements: existing });
      const result = formReducer(state, { type: 'REMOVE_REQUIREMENT', criterionId: 'crit-1' });
      expect(result.requirements.has('crit-1')).toBe(false);
      expect(result.requirements.get('crit-2')).toEqual({ value: 'val2' });
    });

    it('is a no-op for non-existent criterionId', () => {
      const state = makeState();
      const result = formReducer(state, { type: 'REMOVE_REQUIREMENT', criterionId: 'ghost' });
      expect(result.requirements.size).toBe(0);
    });
  });

  describe('UPDATE_REQUIREMENT', () => {
    it('patches value without clearing operator', () => {
      const existing = new Map<string, RequirementEntry>([['crit-1', { value: 'old', operator: '>=' }]]);
      const state = makeState({ requirements: existing });
      const result = formReducer(state, { type: 'UPDATE_REQUIREMENT', criterionId: 'crit-1', patch: { value: 'new' } });
      expect(result.requirements.get('crit-1')).toEqual({ value: 'new', operator: '>=' });
    });

    it('patches operator without clearing value', () => {
      const existing = new Map<string, RequirementEntry>([['crit-1', { value: 42 }]]);
      const state = makeState({ requirements: existing });
      const result = formReducer(state, { type: 'UPDATE_REQUIREMENT', criterionId: 'crit-1', patch: { operator: '<=' } });
      expect(result.requirements.get('crit-1')).toEqual({ value: 42, operator: '<=' });
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
      expect(result.requirements.get('c1')).toEqual({ value: 'true' });
      expect(result.requirements.get('c2')).toEqual({ value: 'high' });
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
      const existing = new Map<string, RequirementEntry>([['old-crit', { value: 'old-val' }]]);
      const template: Template = {
        id: 't1',
        name: 'T',
        entityType: 'request',
        items: [{ id: 'i1', criterionId: 'new-crit', value: 'new-val', templateId: 't1' }],
      };
      const state = makeState({ requirements: existing });
      const result = formReducer(state, { type: 'APPLY_TEMPLATE', template });
      expect(result.requirements.has('old-crit')).toBe(false);
      expect(result.requirements.get('new-crit')).toEqual({ value: 'new-val' });
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
    expect(state.icon).toBeNull();
  });

  it('hydrates icon from an existing Request', () => {
    const request: Request = {
      id: 'r1',
      name: 'r',
      planningMode: 'leaf',
      sortOrder: 0,
      icon: 'hammer',
      minimalDurationValue: 1,
      minimalDurationUnit: 'hours',
      schedulingSettingsApply: true,
      status: 'planned',
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      assignments: [],
    };
    expect(buildInitialState(request).icon).toBe('hammer');
  });

  it('treats missing icon on a Request as null', () => {
    const request: Request = {
      id: 'r1',
      name: 'r',
      planningMode: 'leaf',
      sortOrder: 0,
      minimalDurationValue: 1,
      minimalDurationUnit: 'hours',
      schedulingSettingsApply: true,
      status: 'planned',
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      assignments: [],
    };
    expect(buildInitialState(request).icon).toBeNull();
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
      assignments: [spaceAssignment('space-1')],
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
    expect(state.selectedResourceId).toBe('space-1');
    expect(state.durationValue).toBe(8);
    expect(state.durationUnit).toBe('hours');
    expect(state.schedulingSettingsApply).toBe(false);
    expect(state.requirements.get('c1')).toEqual({ value: 'true', operator: undefined });
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
      assignments: [],
    };
    const state = buildInitialState(request);
    expect(state.name).toBe('Minimal');
    expect(state.description).toBe('');
    expect(state.selectedResourceId).toBe('');
    expect(state.startDate).toBe('');
    expect(state.requirements.size).toBe(0);
  });

  it('sets parentRequestId when no request is provided', () => {
    const state = buildInitialState(null, 'parent-123');
    expect(state.parentRequestId).toBe('parent-123');
    expect(state.name).toBe('');
  });

  it('applies defaultPlanningMode when no request or parent', () => {
    const state = buildInitialState(null, undefined, 'summary');
    expect(state.planningMode).toBe('summary');
  });

  it('applies defaultPlanningMode alongside parentRequestId', () => {
    const state = buildInitialState(null, 'parent-123', 'container');
    expect(state.parentRequestId).toBe('parent-123');
    // defaultPlanningMode takes precedence; the caller is responsible for
    // passing the correct mode when a parent is set.
    expect(state.planningMode).toBe('container');
  });
});

describe('formReducer — UPDATE_REQUIREMENT for missing entry', () => {
  it('defaults to { value: null } when criterionId not yet in map', () => {
    const result = formReducer(
      { requirements: new Map() } as any,
      { type: 'UPDATE_REQUIREMENT', criterionId: 'c-new', patch: { operator: '>=' } },
    );
    expect(result.requirements.get('c-new')).toEqual({ value: null, operator: '>=' });
  });
});

describe('formReducer — APPLY_TEMPLATE with no items', () => {
  it('sets an empty requirements map when template has no items', () => {
    const initial = { requirements: new Map([['c1', { value: true }]]) } as any;
    const result = formReducer(initial, {
      type: 'APPLY_TEMPLATE',
      template: { durationValue: 2, durationUnit: 'hours' } as any,
    });
    expect(result.requirements.size).toBe(0);
    expect(result.durationValue).toBe(2);
  });
});

describe('buildInitialState — defaultSchedule (calendar slot prefill)', () => {
  it('seeds the schedule fields in create mode', () => {
    const state = buildInitialState(null, undefined, undefined, {
      startTs: '2026-04-17T09:00:00Z',
      endTs: '2026-04-17T17:00:00Z',
    });
    expect(state.startDate).toBe('2026-04-17');
    expect(state.endDate).toBe('2026-04-17');
    expect(state.startTime).toMatch(/^\d{2}:\d{2}$/);
    expect(state.endTime).toMatch(/^\d{2}:\d{2}$/);
    expect(state.name).toBe('');
  });

  it("overrides an existing (unscheduled) request's empty schedule", () => {
    const request: Request = {
      id: 'r1',
      name: 'Backlog item',
      planningMode: 'leaf',
      sortOrder: 0,
      minimalDurationValue: 4,
      minimalDurationUnit: 'hours',
      schedulingSettingsApply: true,
      status: 'planned',
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      assignments: [],
    };
    const state = buildInitialState(request, undefined, undefined, {
      startTs: '2026-05-01T08:00:00Z',
      endTs: '2026-05-01T12:00:00Z',
    });
    expect(state.name).toBe('Backlog item');
    expect(state.startDate).toBe('2026-05-01');
    expect(state.endDate).toBe('2026-05-01');
  });

  it('ignores a partial default schedule (start without end)', () => {
    const state = buildInitialState(null, undefined, undefined, {
      startTs: '2026-04-17T09:00:00Z',
    });
    expect(state.startDate).toBe('');
    expect(state.endDate).toBe('');
  });
});

describe('buildInitialState — site scope', () => {
  it('seeds siteId from the active site in create mode', () => {
    const state = buildInitialState(null, undefined, undefined, undefined, 'site-A');
    expect(state.siteId).toBe('site-A');
  });

  it('defaults to site-neutral ("") when no active site is given', () => {
    const state = buildInitialState(null);
    expect(state.siteId).toBe('');
  });

  it('hydrates siteId from an existing request (ignoring the create default)', () => {
    const request: Request = {
      id: 'r1', name: 'r', planningMode: 'leaf', sortOrder: 0,
      siteId: 'site-B',
      minimalDurationValue: 1, minimalDurationUnit: 'hours',
      schedulingSettingsApply: true, status: 'planned',
      createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', assignments: [],
    };
    expect(buildInitialState(request, undefined, undefined, undefined, 'site-A').siteId).toBe('site-B');
  });

  it('pre-selects the schedule-slot site for a site-neutral existing request', () => {
    // "Schedule an existing request" from a site's calendar: a site-neutral
    // backlog request should adopt that site so it lands on the calendar.
    const request: Request = {
      id: 'r1', name: 'r', planningMode: 'leaf', sortOrder: 0,
      minimalDurationValue: 1, minimalDurationUnit: 'hours',
      schedulingSettingsApply: true, status: 'planned',
      createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', assignments: [],
    };
    expect(buildInitialState(request, undefined, undefined, undefined, null, 'site-cal').siteId).toBe('site-cal');
  });

  it('keeps an existing concrete site over the schedule-slot site', () => {
    const request: Request = {
      id: 'r1', name: 'r', planningMode: 'leaf', sortOrder: 0,
      siteId: 'site-B',
      minimalDurationValue: 1, minimalDurationUnit: 'hours',
      schedulingSettingsApply: true, status: 'planned',
      createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', assignments: [],
    };
    expect(buildInitialState(request, undefined, undefined, undefined, null, 'site-cal').siteId).toBe('site-B');
  });

  it('falls back to the schedule-slot site in create mode when no active site', () => {
    expect(buildInitialState(null, undefined, undefined, undefined, null, 'site-cal').siteId).toBe('site-cal');
  });
});
