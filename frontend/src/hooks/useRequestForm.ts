/**
 * Custom hook for managing request form state
 * Consolidates 20+ useState calls into a single useReducer
 * Following DRY/KISS principles for complex form management
 */

import { useEffect, useReducer, useRef } from 'react';
import { useResourceTypes } from '@foundation/src/hooks/useResourceTypes';
import { DEFAULT_START_TIME, DEFAULT_END_TIME, DEFAULT_DURATION_VALUE, DEFAULT_DURATION_UNIT } from '@foundation/src/constants';
import { formatDateForInput, formatTimeForInput } from '@foundation/src/lib/utils';
import { getAssignmentOfType, getTargetResourceTypeKeys } from '@foundation/src/domain/scheduling/request-assignments';
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
  /** The resource types this request needs — one picker, and one assignment, each. */
  targetResourceTypeKeys: string[];
  /** Picked resource per targeted type key. A missing/empty entry means "none yet". */
  selectedResourceIds: Record<string, string>;

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
  targetResourceTypeKeys: [],
  selectedResourceIds: {},
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

/** Optional resource to pre-select (e.g. a grid cell click on that resource's row). */
export interface DefaultResource {
  typeKey: string;
  resourceId: string;
}

/** Overlay a default resource onto a built state: target its type and select it. */
function applyDefaultResource(state: RequestFormState, resource?: DefaultResource): RequestFormState {
  if (!resource) return state;
  return {
    ...state,
    targetResourceTypeKeys: state.targetResourceTypeKeys.includes(resource.typeKey)
      ? state.targetResourceTypeKeys
      : [...state.targetResourceTypeKeys, resource.typeKey],
    selectedResourceIds: { ...state.selectedResourceIds, [resource.typeKey]: resource.resourceId },
  };
}

/** @internal Exported for unit testing */
export function buildInitialState(request?: Request | null, parentRequestId?: string, defaultPlanningMode?: PlanningMode, defaultSchedule?: DefaultSchedule, defaultSiteId?: string | null, scheduleSiteId?: string | null, defaultResource?: DefaultResource, defaultTargets?: string[]): RequestFormState {
  if (request) {
    const reqMap = new Map<string, RequirementEntry>();
    request.requirements?.forEach((r) => {
      reqMap.set(r.criterionId, { value: r.value, operator: r.operator });
    });

    const targetResourceTypeKeys = getTargetResourceTypeKeys(request);
    const selectedResourceIds = Object.fromEntries(
      targetResourceTypeKeys.map((key) => [key, getAssignmentOfType(request, key)?.resourceId ?? '']),
    );

    return applyDefaultSchedule({
      name: request.name,
      description: request.description || '',
      icon: request.icon ?? null,
      planningMode: request.planningMode || 'leaf',
      parentRequestId: request.parentRequestId || '',
      // A site-neutral request scheduled onto a site's calendar pre-selects that
      // site (scheduleSiteId); an existing concrete site is kept untouched.
      siteId: (request.siteId ?? '') || (scheduleSiteId ?? ''),
      targetResourceTypeKeys,
      selectedResourceIds,
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
    return applyDefaultResource(applyDefaultSchedule({ ...initialState, parentRequestId, siteId: createSiteId, ...(defaultTargets ? { targetResourceTypeKeys: defaultTargets } : {}), ...(defaultPlanningMode ? { planningMode: defaultPlanningMode } : {}) }, defaultSchedule), defaultResource);
  }

  return applyDefaultResource(applyDefaultSchedule({ ...initialState, siteId: createSiteId, ...(defaultTargets ? { targetResourceTypeKeys: defaultTargets } : {}), ...(defaultPlanningMode ? { planningMode: defaultPlanningMode } : {}) }, defaultSchedule), defaultResource);
}

export function useRequestForm(request?: Request | null, parentRequestId?: string, defaultPlanningMode?: PlanningMode, defaultSchedule?: DefaultSchedule, defaultSiteId?: string | null, scheduleSiteId?: string | null, defaultResource?: DefaultResource) {
  // A new request defaults to needing ONE place. Every targeted type is a separate assignment
  // the request waits for, so defaulting to all placeable types would mean needing a room *and* a
  // mill *and* a drill. Prefer `space` where it still exists — the historical default, unchanged
  // for anyone who never renamed it — else the tenant's first placeable type. Read from the query
  // cache at mount; while it is cold, the shared constant covers the gap.
  const { data: resourceTypes = [], isSuccess: typesLoaded } = useResourceTypes(true);
  const placeable = resourceTypes.filter((t) => t.hasGeometry);
  const defaultTargetKey = typesLoaded && placeable.length > 0
    ? (placeable.find((t) => t.key === 'space') ?? placeable[0]).key
    : null;
  const [state, dispatch] = useReducer(formReducer, undefined, () => buildInitialState(request, parentRequestId, defaultPlanningMode, defaultSchedule, defaultSiteId, scheduleSiteId, defaultResource, defaultTargetKey ? [defaultTargetKey] : undefined));

  // The types query can still be cold when the dialog mounts, and the reducer's initializer runs
  // once — so without this the form would keep an empty target list for its whole life. Apply the
  // default once, when the types arrive. There is no space fallback to lean on any more.
  // Not when a defaultResource was supplied: buildInitialState already targeted that resource's
  // own type through applyDefaultResource, and overwriting it here would drop the type the user
  // clicked and strand the pre-selected id under a type no longer targeted.
  const defaultApplied = useRef(defaultTargetKey !== null);
  useEffect(() => {
    if (defaultApplied.current || request || defaultResource || defaultTargetKey === null) return;
    defaultApplied.current = true;
    dispatch({ type: 'SET_FIELD', field: 'targetResourceTypeKeys', value: [defaultTargetKey] });
  }, [defaultTargetKey, request, defaultResource]);

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
