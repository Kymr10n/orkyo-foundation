/**
 * Custom hook for managing request form state
 * Consolidates 20+ useState calls into a single useReducer
 * Following DRY/KISS principles for complex form management
 */

import { useReducer } from 'react';
import { DEFAULT_START_TIME, DEFAULT_END_TIME, DEFAULT_DURATION_VALUE, DEFAULT_DURATION_UNIT } from '@/constants';
import { formatDateForInput, formatTimeForInput } from '@/lib/utils';
import type { Request, DurationUnit, PlanningMode } from '@/types/requests';
import type { CriterionValue } from '@/types/criterion';
import type { Template } from '@/types/templates';

export interface RequestFormState {
  // Basic info
  name: string;
  description: string;
  planningMode: PlanningMode;
  parentRequestId: string;
  selectedSpaceId: string;
  
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
  requirements: Map<string, CriterionValue | null>;
  selectedCriterionId: string;
  
  // UI state
  openSections: {
    basic: boolean;
    schedule: boolean;
    constraints: boolean;
    duration: boolean;
    requirements: boolean;
  };
}

type RequestFormAction =
  | { type: 'SET_FIELD'; field: keyof RequestFormState; value: RequestFormState[keyof RequestFormState] }
  | { type: 'TOGGLE_SECTION'; section: keyof RequestFormState['openSections'] }
  | { type: 'ADD_REQUIREMENT'; criterionId: string; value: CriterionValue | null }
  | { type: 'REMOVE_REQUIREMENT'; criterionId: string }
  | { type: 'UPDATE_REQUIREMENT'; criterionId: string; value: CriterionValue | null }
  | { type: 'APPLY_TEMPLATE'; template: Template };

const initialState: RequestFormState = {
  name: '',
  description: '',
  planningMode: 'leaf',
  parentRequestId: '',
  selectedSpaceId: '',
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
  requirements: new Map(),
  selectedCriterionId: '',
  openSections: {
    basic: true,
    schedule: true,
    constraints: false,
    duration: true,
    requirements: true,
  },
};

/** @internal Exported for unit testing */
export function formReducer(state: RequestFormState, action: RequestFormAction): RequestFormState {
  switch (action.type) {
    case 'SET_FIELD':
      return { ...state, [action.field]: action.value };
      
    case 'TOGGLE_SECTION':
      return {
        ...state,
        openSections: {
          ...state.openSections,
          [action.section]: !state.openSections[action.section],
        },
      };
      
    case 'ADD_REQUIREMENT': {
      const newRequirements = new Map(state.requirements);
      newRequirements.set(action.criterionId, action.value);
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
      newRequirements.set(action.criterionId, action.value);
      return { ...state, requirements: newRequirements };
    }
      
    case 'APPLY_TEMPLATE': {
      const reqMap = new Map<string, CriterionValue | null>();
      action.template.items?.forEach((item) => {
        reqMap.set(item.criterionId, item.value);
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

/** @internal Exported for unit testing */
export function buildInitialState(request?: Request | null, parentRequestId?: string, defaultPlanningMode?: PlanningMode): RequestFormState {
  if (request) {
    const reqMap = new Map<string, CriterionValue | null>();
    request.requirements?.forEach((r) => {
      reqMap.set(r.criterionId, r.value);
    });

    return {
      name: request.name,
      description: request.description || '',
      planningMode: request.planningMode || 'leaf',
      parentRequestId: request.parentRequestId || '',
      selectedSpaceId: request.spaceId || '',
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
      openSections: {
        basic: true,
        schedule: true,
        constraints: false,
        duration: true,
        requirements: true,
      },
    };
  }

  if (parentRequestId) {
    return { ...initialState, parentRequestId, ...(defaultPlanningMode ? { planningMode: defaultPlanningMode } : {}) };
  }

  return defaultPlanningMode ? { ...initialState, planningMode: defaultPlanningMode } : initialState;
}

export function useRequestForm(request?: Request | null, parentRequestId?: string, defaultPlanningMode?: PlanningMode) {
  const [state, dispatch] = useReducer(formReducer, undefined, () => buildInitialState(request, parentRequestId, defaultPlanningMode));

  return {
    state,
    setField: (field: keyof RequestFormState, value: RequestFormState[keyof RequestFormState]) =>
      dispatch({ type: 'SET_FIELD', field, value }),
    toggleSection: (section: keyof RequestFormState['openSections']) =>
      dispatch({ type: 'TOGGLE_SECTION', section }),
    addRequirement: (criterionId: string, value: CriterionValue | null) =>
      dispatch({ type: 'ADD_REQUIREMENT', criterionId, value }),
    removeRequirement: (criterionId: string) =>
      dispatch({ type: 'REMOVE_REQUIREMENT', criterionId }),
    updateRequirement: (criterionId: string, value: CriterionValue | null) =>
      dispatch({ type: 'UPDATE_REQUIREMENT', criterionId, value }),
    applyTemplate: (template: Template) =>
      dispatch({ type: 'APPLY_TEMPLATE', template }),
  };
}
