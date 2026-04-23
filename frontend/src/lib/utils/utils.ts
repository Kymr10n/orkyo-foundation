import { type ClassValue, clsx } from "clsx"
import { twMerge } from "tailwind-merge"
import type { RequestFormData } from "@foundation/src/components/requests/RequestFormDialog"
import type { CreateRequestRequest, UpdateRequestRequest } from "@foundation/src/types/requests"

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
    case "planned":
      return "bg-blue-500/10 text-blue-700 dark:text-blue-400";
    case "in_progress":
      return "bg-yellow-500/10 text-yellow-700 dark:text-yellow-400";
    case "done":
      return "bg-green-500/10 text-green-700 dark:text-green-400";
    case "cancelled":
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
    case "planned":
      return "bg-blue-500";
    case "in_progress":
      return "bg-yellow-500";
    case "done":
      return "bg-green-500";
    case "cancelled":
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
    case "planned":
      return "Planned";
    case "in_progress":
      return "In Progress";
    case "done":
      return "Done";
    case "cancelled":
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
 */
export function buildUpdatePayload(data: RequestFormData): UpdateRequestRequest {
  return {
    name: data.name,
    description: data.description,
    planningMode: data.planningMode,
    spaceId: data.spaceId,
    startTs: data.startTs,
    endTs: data.endTs,
    earliestStartTs: data.earliestStartTs,
    latestEndTs: data.latestEndTs,
    minimalDurationValue: data.duration.value,
    minimalDurationUnit: data.duration.unit,
    schedulingSettingsApply: data.schedulingSettingsApply,
    requirements: data.requirements,
  };
}

/**
 * Build a CreateRequestRequest from form data.
 */
export function buildCreatePayload(data: RequestFormData): CreateRequestRequest {
  return {
    name: data.name,
    description: data.description,
    planningMode: data.planningMode,
    parentRequestId: data.parentRequestId,
    spaceId: data.spaceId,
    startTs: data.startTs,
    endTs: data.endTs,
    earliestStartTs: data.earliestStartTs,
    latestEndTs: data.latestEndTs,
    minimalDurationValue: data.duration.value,
    minimalDurationUnit: data.duration.unit,
    schedulingSettingsApply: data.schedulingSettingsApply,
    requirements: data.requirements.map((req) => ({
      criterionId: req.criterionId,
      value: req.value,
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
