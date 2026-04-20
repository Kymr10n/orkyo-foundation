// Types
export type {
  SchedulingSettings,
  OffTimeType,
  OffTimeDefinition,
  OffTimeRange,
  EffectiveCalendar,
} from "./types";

// Recurrence
export { expandRecurrence, parseRRule } from "./recurrence";

// Weekend ranges
export { generateWeekendRanges } from "./weekend-ranges";

// Working-time predicates
export {
  isWorkingTime,
  nextWorkingStart,
  workingSegmentEnd,
} from "./working-time";

// Duration calculator
export {
  computeWorkingDuration,
} from "./duration-calculator";


// Preview/render pipeline (was previously in domain/schedule-*.ts)
export { buildPreviewSchedule } from "./schedule-preview";
export { buildIndex, getOverlapping, getStackIndex, getOverlapGroupSize, getMaxOverlapInSpace } from "./schedule-index";
export { evaluateSchedule, hasConflicts, getAllConflicts } from "./schedule-validator";
export { selectRequestDisplayData, selectSpaceOverlapCount, isOutsideView } from "./schedule-selectors";
export { durationToMs } from "./schedule-model";
export type {
  PreviewEntry,
  PreviewSchedule,
  DraftInteraction,
  DraftResize,
  ResizeEdge,
  ResizePhase,
  ValidationResult,
} from "./schedule-model";
export type { ScheduleIndex } from "./schedule-index";


// Capability matcher (space ↔ request requirements)
export { validateSpaceRequirements } from "./capability-matcher";
