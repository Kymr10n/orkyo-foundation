import { type ClassValue, clsx } from "clsx"
import { twMerge } from "tailwind-merge"
import type { RequestFormData } from "@foundation/src/components/requests/RequestFormDialog"
import type { CreateRequestRequest, PlanningMode, UpdateRequestRequest } from "@foundation/src/types/requests"
import { REQUEST_STATUS } from "@foundation/src/constants/request-status"

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

/**
 * Format a Date object to ISO date string (YYYY-MM-DD)
 */
export function formatDateForInput(date: Date): string {
  return date.toISOString().split("T")[0];
}

/**
 * Format a Date object to time string (HH:MM)
 */
export function formatTimeForInput(date: Date): string {
  return date.toTimeString().slice(0, 5);
}

/**
 * Combine date and time strings into ISO datetime string
 */
export function combineDateTimeToISO(date: string, time: string): string {
  return new Date(`${date}T${time}`).toISOString();
}

/**
 * Get Tailwind color classes for criterion data types
 */
/**
 * Returns true if value contains only alphanumeric characters, underscores, and hyphens.
 * Used to validate codes and identifiers (site codes, criterion names, etc.)
 */
export function isValidSlug(value: string): boolean {
  return /^[a-zA-Z0-9_-]+$/.test(value);
}

export function getDataTypeColor(dataType: string): string {
  switch (dataType) {
    case "Boolean":
      return "bg-blue-500/10 text-blue-700 dark:text-blue-400";
    case "Number":
      return "bg-green-500/10 text-green-700 dark:text-green-400";
    case "String":
      return "bg-purple-500/10 text-purple-700 dark:text-purple-400";
    case "Date":
      return "bg-indigo-500/10 text-indigo-700 dark:text-indigo-400";
    case "Enum":
      return "bg-orange-500/10 text-orange-700 dark:text-orange-400";
    default:
      return "bg-muted text-muted-foreground";
  }
}

/**
 * Format a duration value+unit pair into a human-readable string.
 * e.g. (2, "hours") → "2 hours", (1, "days") → "1 day"
 */
export function formatDuration(value: number, unit: string): string {
  const label = value === 1 ? unit.replace(/s$/, "") : unit;
  return `${value} ${label}`;
}

/**
 * Format an ISO date string for display.
 * e.g. "2026-04-02T10:30:00Z" → "02/04/2026"
 */
export function formatDateDisplay(dateStr?: string | null): string {
  if (!dateStr) return "-";
  const date = new Date(dateStr);
  return date.toLocaleDateString();
}

/**
 * Get Tailwind color classes for request status badges.
 */
export function getStatusColor(status: string): string {
  switch (status) {
    case REQUEST_STATUS.PLANNED:
      return "bg-blue-500/10 text-blue-700 dark:text-blue-400";
    case REQUEST_STATUS.IN_PROGRESS:
      return "bg-yellow-500/10 text-yellow-700 dark:text-yellow-400";
    case REQUEST_STATUS.DONE:
      return "bg-green-500/10 text-green-700 dark:text-green-400";
    case REQUEST_STATUS.CANCELLED:
      return "bg-muted text-muted-foreground line-through";
    default:
      return "bg-muted text-muted-foreground";
  }
}

/**
 * Get Tailwind bg class for a small status dot indicator.
 */
export function getStatusDotColor(status: string): string {
  switch (status) {
    case REQUEST_STATUS.PLANNED:
      return "bg-blue-500";
    case REQUEST_STATUS.IN_PROGRESS:
      return "bg-yellow-500";
    case REQUEST_STATUS.DONE:
      return "bg-green-500";
    case REQUEST_STATUS.CANCELLED:
      return "bg-gray-400";
    default:
      return "bg-gray-400";
  }
}

/**
 * Human-readable label for a RequestStatus wire value.
 */
export function formatStatusLabel(status: string): string {
  switch (status) {
    case REQUEST_STATUS.PLANNED:
      return "Planned";
    case REQUEST_STATUS.IN_PROGRESS:
      return "In Progress";
    case REQUEST_STATUS.DONE:
      return "Done";
    case REQUEST_STATUS.CANCELLED:
      return "Cancelled";
    default:
      return status;
  }
}

// ---------------------------------------------------------------------------
// Request form data → API payload helpers (shared between pages)
// ---------------------------------------------------------------------------

/**
 * Build an UpdateRequestRequest from form data.
 *
 * Pass `originalPlanningMode` (the request's mode when the form opened) so the Type is
 * sent only when the user actually changed it. The backend treats an absent
 * `planningMode` as "keep existing" (UpdateRequestRequest.PlanningMode is nullable), so
 * an unrelated edit no longer re-asserts the mode and trips the "cannot change to leaf
 * while it has children" guard. Omit the argument to always send it (e.g. create-derived
 * flows that have no prior mode).
 */
export function buildUpdatePayload(
  data: RequestFormData,
  originalPlanningMode?: PlanningMode,
): UpdateRequestRequest {
  const planningModeChanged =
    originalPlanningMode === undefined || data.planningMode !== originalPlanningMode;
  return {
    name: data.name,
    description: data.description,
    icon: data.icon,
    planningMode: planningModeChanged ? data.planningMode : undefined,
    resourceId: data.resourceId,
    startTs: data.startTs,
    endTs: data.endTs,
    earliestStartTs: data.earliestStartTs,
    latestEndTs: data.latestEndTs,
    minimalDurationValue: data.duration.value,
    minimalDurationUnit: data.duration.unit,
    schedulingSettingsApply: data.schedulingSettingsApply,
    requirements: data.requirements
      .filter((req) => req.value !== null)
      .map((req) => ({
        criterionId: req.criterionId,
        value: req.value!,
        ...(req.operator !== undefined && { operator: req.operator }),
      })),
  };
}

/**
 * Build a CreateRequestRequest from form data.
 */
export function buildCreatePayload(data: RequestFormData): CreateRequestRequest {
  return {
    name: data.name,
    description: data.description,
    icon: data.icon,
    planningMode: data.planningMode,
    parentRequestId: data.parentRequestId,
    resourceId: data.resourceId,
    startTs: data.startTs,
    endTs: data.endTs,
    earliestStartTs: data.earliestStartTs,
    latestEndTs: data.latestEndTs,
    minimalDurationValue: data.duration.value,
    minimalDurationUnit: data.duration.unit,
    schedulingSettingsApply: data.schedulingSettingsApply,
    requirements: data.requirements
      .filter((req) => req.value !== null)
      .map((req) => ({
        criterionId: req.criterionId,
        value: req.value!,
        ...(req.operator !== undefined && { operator: req.operator }),
      })),
  };
}

/**
 * Format a raw minutes count into a compact human-readable string.
 * e.g. 90 → "1h 30m", 2880 → "2d", 10080 → "1w"
 */
export function formatMinutesHuman(totalMinutes: number): string {
  if (totalMinutes < 60) return `${totalMinutes}m`;
  if (totalMinutes < 1440) {
    const h = Math.floor(totalMinutes / 60);
    const m = totalMinutes % 60;
    return m > 0 ? `${h}h ${m}m` : `${h}h`;
  }
  const d = Math.floor(totalMinutes / 1440);
  const remainH = Math.floor((totalMinutes % 1440) / 60);
  if (d < 7) return remainH > 0 ? `${d}d ${remainH}h` : `${d}d`;
  const w = Math.floor(d / 7);
  const remainD = d % 7;
  return remainD > 0 ? `${w}w ${remainD}d` : `${w}w`;
}
