import { describe, it, expect } from 'vitest';
import { isRequestFormValidationError, validateRequestForm } from './request-form-validation';
import type { RequestFormState, RequirementEntry } from '@foundation/src/hooks/useRequestForm';
import { VALIDATION_MESSAGES } from '@foundation/src/constants';
import type { RequestFormData } from '@foundation/src/types/requests';

function makeState(overrides: Partial<RequestFormState> = {}): RequestFormState {
  return {
    name: 'Bracket run',
    description: '',
    icon: null,
    planningMode: 'leaf',
    parentRequestId: '',
    siteId: '',
    targetResourceTypeKeys: [],
    selectedResourceIds: {},
    startDate: '',
    startTime: '08:00',
    endDate: '',
    endTime: '17:00',
    earliestStartDate: '',
    earliestStartTime: '',
    latestEndDate: '',
    latestEndTime: '',
    durationValue: 2,
    durationUnit: 'hours',
    schedulingSettingsApply: true,
    requirements: new Map<string, RequirementEntry>(),
    ...overrides,
  };
}

function refusal(overrides: Partial<RequestFormState>) {
  const result = validateRequestForm(makeState(overrides));
  if (!isRequestFormValidationError(result)) throw new Error('expected a refusal');
  return result;
}

function payload(overrides: Partial<RequestFormState> = {}): RequestFormData {
  const result = validateRequestForm(makeState(overrides));
  if (isRequestFormValidationError(result)) throw new Error(`unexpected refusal: ${result.message}`);
  return result;
}

const WINDOW = { startDate: '2026-01-06', startTime: '08:00', endDate: '2026-01-06', endTime: '12:00' };

describe('validateRequestForm refusals', () => {
  it('requires a name, on the Details tab', () => {
    expect(refusal({ name: '   ' })).toEqual({ tab: 'details', message: VALIDATION_MESSAGES.REQUEST_NAME_REQUIRED });
  });

  it('requires a duration of at least 1 for a task', () => {
    expect(refusal({ durationValue: 0 })).toEqual({ tab: 'timing', message: VALIDATION_MESSAGES.DURATION_REQUIRED });
  });

  it('does not ask a group for a duration', () => {
    expect(payload({ planningMode: 'summary', durationValue: 0 }).name).toBe('Bracket run');
  });

  it('refuses an end at or before the start', () => {
    expect(refusal({ ...WINDOW, endTime: '08:00' }).message).toBe(VALIDATION_MESSAGES.END_BEFORE_START);
  });

  it('refuses a start without an end', () => {
    expect(refusal({ startDate: '2026-01-06' }).message).toBe(VALIDATION_MESSAGES.DATES_MUST_BE_TOGETHER);
  });

  it('refuses constraints in the wrong order', () => {
    expect(
      refusal({
        earliestStartDate: '2026-01-10', earliestStartTime: '08:00',
        latestEndDate: '2026-01-05', latestEndTime: '08:00',
      }).message,
    ).toBe(VALIDATION_MESSAGES.CONSTRAINT_ORDER);
  });

  it('refuses a start before the earliest start', () => {
    expect(
      refusal({ ...WINDOW, earliestStartDate: '2026-01-07', earliestStartTime: '08:00' }).message,
    ).toBe(VALIDATION_MESSAGES.START_BEFORE_CONSTRAINT);
  });

  it('refuses an end after the latest end', () => {
    expect(
      refusal({ ...WINDOW, latestEndDate: '2026-01-05', latestEndTime: '08:00' }).message,
    ).toBe(VALIDATION_MESSAGES.END_AFTER_CONSTRAINT);
  });
});

describe('validateRequestForm payload', () => {
  it('trims, and sends a task its schedule, its picks and its set requirements', () => {
    const data = payload({
      ...WINDOW,
      name: '  Bracket run  ',
      description: '  ',
      targetResourceTypeKeys: ['space', 'vehicle', 'person'],
      selectedResourceIds: { space: 'room-1', vehicle: 'van-1' },
      requirements: new Map<string, RequirementEntry>([
        ['crit-1', { value: 5, operator: 'gte' } as RequirementEntry],
        ['crit-2', { value: null }],
      ]),
    });

    expect(data).toMatchObject({
      name: 'Bracket run',
      description: undefined,
      siteId: null,
      parentRequestId: undefined,
      resourceIds: ['room-1', 'van-1'],
      startTs: new Date('2026-01-06T08:00').toISOString(),
      endTs: new Date('2026-01-06T12:00').toISOString(),
      duration: { value: 2, unit: 'hours' },
    });
    expect(data.requirements).toEqual([{ criterionId: 'crit-1', value: 5, operator: 'gte' }]);
  });

  it('sends a derived group neither schedule, constraints nor picks', () => {
    const data = payload({
      ...WINDOW,
      planningMode: 'summary',
      earliestStartDate: '2026-01-01', earliestStartTime: '08:00',
      selectedResourceIds: { space: 'room-1' },
      targetResourceTypeKeys: ['space'],
    });
    expect(data.startTs).toBeUndefined();
    expect(data.endTs).toBeUndefined();
    expect(data.earliestStartTs).toBeUndefined();
    expect(data.resourceIds).toBeUndefined();
  });

  it('sends a boundary group its constraints but not a schedule', () => {
    const data = payload({
      ...WINDOW,
      planningMode: 'container',
      earliestStartDate: '2026-01-01', earliestStartTime: '08:00',
      latestEndDate: '2026-02-01', latestEndTime: '08:00',
    });
    expect(data.startTs).toBeUndefined();
    expect(data.earliestStartTs).toBe(new Date('2026-01-01T08:00').toISOString());
    expect(data.latestEndTs).toBe(new Date('2026-02-01T08:00').toISOString());
  });
});
