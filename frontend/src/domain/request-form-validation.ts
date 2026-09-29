import { VALIDATION_MESSAGES } from "@foundation/src/constants";
import { combineDateTimeToISO } from "@foundation/src/lib/utils";
import type { RequestFormData } from "@foundation/src/types/requests";
import type { RequestFormState } from "@foundation/src/hooks/useRequestForm";

export type RequestFormTab = 'details' | 'timing' | 'requirements' | 'resources' | 'children' | 'dependencies';

/** A refusal, named with the tab that owns the offending field so the dialog can switch to it. */
export interface RequestFormValidationError {
  tab: RequestFormTab;
  message: string;
}

export function isRequestFormValidationError(
  result: RequestFormValidationError | RequestFormData,
): result is RequestFormValidationError {
  return 'message' in result && 'tab' in result;
}

/**
 * Checks the request form and, when it passes, builds the save payload.
 *
 * Pure: it reads only `state`. What is editable follows the planning mode — a leaf (Task)
 * edits its own schedule and constraints, a boundary group (container) only its constraints,
 * a derived group (summary) neither — so fields a mode cannot edit are never sent.
 */
export function validateRequestForm(state: RequestFormState): RequestFormValidationError | RequestFormData {
  const isLeaf = state.planningMode === 'leaf';
  const hasEditableSchedule = isLeaf;
  const hasEditableConstraints = isLeaf || state.planningMode === 'container';

  if (!state.name.trim()) {
    return { tab: 'details', message: VALIDATION_MESSAGES.REQUEST_NAME_REQUIRED };
  }

  if (hasEditableSchedule && (!state.durationValue || state.durationValue < 1)) {
    return { tab: 'timing', message: VALIDATION_MESSAGES.DURATION_REQUIRED };
  }

  // Scheduling dates, if provided (leaf only)
  const startTs = hasEditableSchedule && state.startDate && state.startTime
    ? combineDateTimeToISO(state.startDate, state.startTime)
    : undefined;
  const endTs = hasEditableSchedule && state.endDate && state.endTime
    ? combineDateTimeToISO(state.endDate, state.endTime)
    : undefined;

  if (startTs && endTs && new Date(startTs) >= new Date(endTs)) {
    return { tab: 'timing', message: VALIDATION_MESSAGES.END_BEFORE_START };
  }

  if ((startTs && !endTs) || (!startTs && endTs)) {
    return { tab: 'timing', message: VALIDATION_MESSAGES.DATES_MUST_BE_TOGETHER };
  }

  // Constraint dates, if provided (leaf and boundary-group modes)
  const earliestStartTs = hasEditableConstraints && state.earliestStartDate && state.earliestStartTime
    ? combineDateTimeToISO(state.earliestStartDate, state.earliestStartTime)
    : undefined;
  const latestEndTs = hasEditableConstraints && state.latestEndDate && state.latestEndTime
    ? combineDateTimeToISO(state.latestEndDate, state.latestEndTime)
    : undefined;

  if (earliestStartTs && latestEndTs && new Date(earliestStartTs) >= new Date(latestEndTs)) {
    return { tab: 'timing', message: VALIDATION_MESSAGES.CONSTRAINT_ORDER };
  }

  // Scheduled dates must sit within the constraints
  if (earliestStartTs && startTs && new Date(startTs) < new Date(earliestStartTs)) {
    return { tab: 'timing', message: VALIDATION_MESSAGES.START_BEFORE_CONSTRAINT };
  }

  if (latestEndTs && endTs && new Date(endTs) > new Date(latestEndTs)) {
    return { tab: 'timing', message: VALIDATION_MESSAGES.END_AFTER_CONSTRAINT };
  }

  // One id per targeted type that actually has a pick. Ordered by the target list so the
  // payload is stable across saves; the backend routes each id by its own resource's type.
  const pickedResourceIds = state.targetResourceTypeKeys
    .map((key) => state.selectedResourceIds[key])
    .filter((id): id is string => Boolean(id));

  return {
    name: state.name.trim(),
    description: state.description.trim() || undefined,
    icon: state.icon ?? null,
    planningMode: state.planningMode,
    parentRequestId: state.parentRequestId || undefined,
    siteId: state.siteId || null,
    // Every pick travels with the save, so a request needing a room and a van is never
    // left half-assigned by a second call failing.
    resourceIds: isLeaf ? pickedResourceIds : undefined,
    targetResourceTypeKeys: state.targetResourceTypeKeys,
    startTs,
    endTs,
    earliestStartTs,
    latestEndTs,
    duration: {
      value: state.durationValue,
      unit: state.durationUnit,
    },
    schedulingSettingsApply: state.schedulingSettingsApply,
    requirements: Array.from(state.requirements.entries())
      .filter(([, entry]) => entry.value !== null)
      .map(([criterionId, entry]) => ({
        criterionId,
        value: entry.value!,
        operator: entry.operator,
      })),
  };
}
