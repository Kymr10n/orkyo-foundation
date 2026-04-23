/**
 * Application-wide constants
 */

import { FileText, Layers } from "lucide-react";
import type { PlanningMode } from "@foundation/src/types/requests";

// Time defaults
export const DEFAULT_START_TIME = "09:00";
export const DEFAULT_END_TIME = "17:00";

// Duration defaults
export const DEFAULT_DURATION_VALUE = 1;
export const DEFAULT_DURATION_UNIT = "days";

// Form placeholders
export const SPACE_NONE_PLACEHOLDER = "__none__";

// Planning mode display config (user-facing names, NOT backend enum values)
export const PLANNING_MODE_CONFIG: Record<PlanningMode, {
  label: string;
  icon: typeof FileText;
  description: string;
}> = {
  leaf:      { label: "Task",      icon: FileText,   description: "A single task that can be placed on the scheduler." },
  summary:   { label: "Group",     icon: Layers,     description: "Groups child tasks with derived timing from children." },
  container: { label: "Group", icon: Layers,  description: "Groups child tasks with optional boundary constraints." },
};

export const getPlanningModeLabel = (mode: PlanningMode): string =>
  PLANNING_MODE_CONFIG[mode]?.label ?? mode;

export const getPlanningModeIcon = (mode: PlanningMode) =>
  PLANNING_MODE_CONFIG[mode]?.icon ?? FileText;

// Validation messages
export const VALIDATION_MESSAGES = {
  REQUEST_NAME_REQUIRED: "Request name is required",
  DURATION_REQUIRED: "Duration must be at least 1",
  END_BEFORE_START: "End date/time must be after start date/time",
  DATES_MUST_BE_TOGETHER: "Both start and end dates must be provided together, or leave both blank for unscheduled request",
  CONSTRAINT_ORDER: "Earliest start must be before latest end",
  START_BEFORE_CONSTRAINT: "Start date/time must be on or after earliest start constraint",
  END_AFTER_CONSTRAINT: "End date/time must be on or before latest end constraint",
  SAVE_FAILED: "Failed to save request",
} as const;
