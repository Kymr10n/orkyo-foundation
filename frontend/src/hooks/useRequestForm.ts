/**
 * Custom hook for managing request form state
 * Consolidates 20+ useState calls into a single useReducer
 * Following DRY/KISS principles for complex form management
 */

import { useReducer } from 'react';
import { DEFAULT_START_TIME, DEFAULT_END_TIME, DEFAULT_DURATION_VALUE, DEFAULT_DURATION_UNIT } from '@foundation/src/constants';
import { formatDateForInput, formatTimeForInput } from '@foundation/src/lib/utils';
import { getSpaceResourceId } from '@foundation/src/domain/scheduling/request-assignments';
import type { Request, DurationUnit, PlanningMode } from '@foundation/src/types/requests';
import type { CriterionValue } from '@foundation/src/types/criterion';
import type { Template } from '@foundation/src/types/templates';

export interface RequirementEntry {
  value: CriterionValue | null;
  operator?: string;
}

export interface RequestFormState {
  // Basic info
  name: string;
  description: string;
  icon: string | null;
  planningMode: PlanningMode;
  parentRequestId: string;
  /** Site scope. '' = site-neutral (Any site). */
  siteId: string;
  selectedResourceId: string;

  // Schedule
  startDate: string;
  startTime: string;
  endDate: string;
  endTime: string;

  // Constraints
  earliestStartDate: string;
  earliestStartTime: string;
  latestEndDate: string;
  latestEndTime: string;

  // Duration
  durationValue: number;
  durationUnit: DurationUnit;

  // Scheduling
  schedulingSettingsApply: boolean;

  // Requirements
  requirements: Map<string, RequirementEntry>;
  selectedCriterionId: string;
}

type RequestFormAction =
  | { type: 'SET_FIELD'; field: keyof RequestFormState; value: RequestFormState[keyof RequestFormState] }
  | { type: 'ADD_REQUIREMENT'; criterionId: string; value: CriterionValue | null }
  | { type: 'REMOVE_REQUIREMENT'; criterionId: string }
  | { type: 'UPDATE_REQUIREMENT'; criterionId: string; patch: Partial<RequirementEntry> }
  | { type: 'APPLY_TEMPLATE'; template: Template };

const initialState: RequestFormState = {
  name: '',
  description: '',
  icon: null,
  planningMode: 'leaf',
  parentRequestId: '',
  siteId: '',
  selectedResourceId: '',
  startDate: '',
  startTime: DEFAULT_START_TIME,
  endDate: '',
  endTime: DEFAULT_END_TIME,
  earliestStartDate: '',
  earliestStartTime: '',
  latestEndDate: '',
  latestEndTime: '',
  durationValue: DEFAULT_DURATION_VALUE,
  durationUnit: DEFAULT_DURATION_UNIT as DurationUnit,
  schedulingSettingsApply: true,
  requirements: new Map<string, RequirementEntry>(),
  selectedCriterionId: '',
};

/** @internal Exported for unit testing */
export function formReducer(state: RequestFormState, action: RequestFormAction): RequestFormState {
  switch (action.type) {
    case 'SET_FIELD':
      return { ...state, [action.field]: action.value };

    case 'ADD_REQUIREMENT': {
      const newRequirements = new Map(state.requirements);
      newRequirements.set(action.criterionId, { value: action.value });
      return {
        ...state,
        requirements: newRequirements,
        selectedCriterionId: '',
      };
    }

    case 'REMOVE_REQUIREMENT': {
      const newRequirements = new Map(state.requirements);
      newRequirements.delete(action.criterionId);
      return { ...state, requirements: newRequirements };
    }

    case 'UPDATE_REQUIREMENT': {
      const newRequirements = new Map(state.requirements);
      const existing = newRequirements.get(action.criterionId) ?? { value: null };
      newRequirements.set(action.criterionId, { ...existing, ...action.patch });
      return { ...state, requirements: newRequirements };
    }

    case 'APPLY_TEMPLATE': {
      const reqMap = new Map<string, RequirementEntry>();
      action.template.items?.forEach((item) => {
        reqMap.set(item.criterionId, { value: item.value });
      });
      return {
        ...state,
        durationValue: action.template.durationValue || 1,
        durationUnit: (action.template.durationUnit || 'hours') as DurationUnit,
        schedulingSettingsApply: state.schedulingSettingsApply,
        requirements: reqMap,
      };
    }

    default:
      return state;
  }
}

/** Optional start/end to seed the schedule fields (e.g. a calendar slot selection). */
export interface DefaultSchedule {
  startTs?: string | null;
  endTs?: string | null;
}

/** Overlay a default schedule onto a built state, overriding the start/end fields. */
function applyDefaultSchedule(state: RequestFormState, schedule?: DefaultSchedule): RequestFormState {
  if (!schedule?.startTs || !schedule?.endTs) return state;
  const start = new Date(schedule.startTs);
  const end = new Date(schedule.endTs);
  return {
    ...state,
    startDate: formatDateForInput(start),
    startTime: formatTimeForInput(start),
    endDate: formatDateForInput(end),
    endTime: formatTimeForInput(end),
  };
}

/** @internal Exported for unit testing */
export function buildInitialState(request?: Request | null, parentRequestId?: string, defaultPlanningMode?: PlanningMode, defaultSchedule?: DefaultSchedule, defaultSiteId?: string | null, scheduleSiteId?: string | null): RequestFormState {
  if (request) {
    const reqMap = new Map<string, RequirementEntry>();
    request.requirements?.forEach((r) => {
      reqMap.set(r.criterionId, { value: r.value, operator: r.operator });
    });

    return applyDefaultSchedule({
      name: request.name,
      description: request.description || '',
      icon: request.icon ?? null,
      planningMode: request.planningMode || 'leaf',
      parentRequestId: request.parentRequestId || '',
      // A site-neutral request scheduled onto a site's calendar pre-selects that
      // site (scheduleSiteId); an existing concrete site is kept untouched.
      siteId: (request.siteId ?? '') || (scheduleSiteId ?? ''),
      selectedResourceId: getSpaceResourceId(request) || '',
      startDate: request.startTs ? formatDateForInput(new Date(request.startTs)) : '',
      startTime: request.startTs ? formatTimeForInput(new Date(request.startTs)) : DEFAULT_START_TIME,
      endDate: request.endTs ? formatDateForInput(new Date(request.endTs)) : '',
      endTime: request.endTs ? formatTimeForInput(new Date(request.endTs)) : DEFAULT_END_TIME,
      earliestStartDate: request.earliestStartTs ? formatDateForInput(new Date(request.earliestStartTs)) : '',
      earliestStartTime: request.earliestStartTs ? formatTimeForInput(new Date(request.earliestStartTs)) : '',
      latestEndDate: request.latestEndTs ? formatDateForInput(new Date(request.latestEndTs)) : '',
      latestEndTime: request.latestEndTs ? formatTimeForInput(new Date(request.latestEndTs)) : '',
      durationValue: request.minimalDurationValue,
      durationUnit: request.minimalDurationUnit,
      schedulingSettingsApply: request.schedulingSettingsApply ?? true,
      requirements: reqMap,
      selectedCriterionId: '',
    }, defaultSchedule);
  }

  // Create mode: default to the active site (caller passes selectedSiteId) so a new request is
  // scoped to where the user is working; fall back to the schedule slot's site, else '' = site-neutral.
  const createSiteId = (defaultSiteId ?? '') || (scheduleSiteId ?? '');
  if (parentRequestId) {
    return applyDefaultSchedule({ ...initialState, parentRequestId, siteId: createSiteId, ...(defaultPlanningMode ? { planningMode: defaultPlanningMode } : {}) }, defaultSchedule);
  }

  return applyDefaultSchedule({ ...initialState, siteId: createSiteId, ...(defaultPlanningMode ? { planningMode: defaultPlanningMode } : {}) }, defaultSchedule);
}

export function useRequestForm(request?: Request | null, parentRequestId?: string, defaultPlanningMode?: PlanningMode, defaultSchedule?: DefaultSchedule, defaultSiteId?: string | null, scheduleSiteId?: string | null) {
  const [state, dispatch] = useReducer(formReducer, undefined, () => buildInitialState(request, parentRequestId, defaultPlanningMode, defaultSchedule, defaultSiteId, scheduleSiteId));

  return {
    state,
    setField: (field: keyof RequestFormState, value: RequestFormState[keyof RequestFormState]) =>
      dispatch({ type: 'SET_FIELD', field, value }),
    addRequirement: (criterionId: string, value: CriterionValue | null) =>
      dispatch({ type: 'ADD_REQUIREMENT', criterionId, value }),
    removeRequirement: (criterionId: string) =>
      dispatch({ type: 'REMOVE_REQUIREMENT', criterionId }),
    updateRequirement: (criterionId: string, patch: Partial<RequirementEntry>) =>
      dispatch({ type: 'UPDATE_REQUIREMENT', criterionId, patch }),
    applyTemplate: (template: Template) =>
      dispatch({ type: 'APPLY_TEMPLATE', template }),
  };
}
