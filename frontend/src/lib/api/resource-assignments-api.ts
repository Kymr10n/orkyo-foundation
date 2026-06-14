/**
 * API client for Resource Assignment operations
 */

import { apiGet, apiPost, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

export interface ResourceAssignmentInfo {
  id: string;
  requestId: string;
  resourceId: string;
  resourceTypeKey: string;
  startUtc: string;
  endUtc: string;
  allocationPercent?: number;
  allocationUnits?: number;
  assignmentStatus: string;
  createdAt: string;
  updatedAt: string;
}

export interface CreateResourceAssignmentRequest {
  requestId: string;
  resourceId: string;
  startUtc: string;
  endUtc: string;
  allocationPercent?: number;
  allocationUnits?: number;
}

export interface ValidateResourceAssignmentRequest {
  /**
   * Optional — when omitted, the validator runs in "dry-run" mode:
   * capability checks are skipped (no request to draw requirements from)
   * but off-time, weekend/holiday, and overbook checks still apply.
   * Use this for pre-save validation in the Add Person dialog.
   */
  requestId?: string;
  resourceId: string;
  startUtc: string;
  endUtc: string;
  allocationPercent?: number;
  allocationUnits?: number;
  allocationMode?: string;
  /**
   * Optional id of an existing assignment to exclude from overbook checks.
   * Set when re-validating an already-committed assignment (the conflicts view)
   * so it does not overlap with itself.
   */
  excludeAssignmentId?: string;
}

export type ValidationSeverity = 'ok' | 'warning' | 'blocker';

export type ValidationReasonCode =
  | 'resource.not-found'
  | 'resource.inactive'
  | 'resource.type-mismatch'
  | 'capability.missing'
  | 'offtime.overlap'
  | 'assignment.overbooked'
  | 'nonworking.weekend'
  | 'nonworking.holiday'
  | 'allocation-mode.invalid'
  | 'allocation-percent.invalid'
  | 'site.mismatch-space'
  | 'site.mismatch-person'
  | 'site.cross-not-allowed';

export interface ValidationIssue {
  code: ValidationReasonCode;
  message: string;
  resourceId?: string;
  conflictingAssignmentId?: string;
  conflictingOffTimeId?: string;
  criterionId?: string;
  details?: string;
}

export interface ValidationResult {
  severity: ValidationSeverity;
  blockers: ValidationIssue[];
  warnings: ValidationIssue[];
}

/**
 * Reason codes the backend surfaces but never hard-blocks a manual assignment on
 * (mirrors ResourceAssignmentService.SoftBlockerCodes). The validate endpoint still
 * returns them under `blockers`; the create endpoint accepts them. The UI mirrors
 * this: it shows them as warnings and allows the assignment to be saved.
 */
export const SOFT_BLOCKER_CODES: ReadonlySet<ValidationReasonCode> = new Set<ValidationReasonCode>([
  'capability.missing',
  'assignment.overbooked',
]);

/** Blockers that genuinely prevent a manual assignment (soft blockers excluded). */
export function hardBlockers(result: ValidationResult): ValidationIssue[] {
  return result.blockers.filter((b) => !SOFT_BLOCKER_CODES.has(b.code));
}

/** Soft blockers — shown to the planner as warnings; they do not prevent saving. */
export function softBlockers(result: ValidationResult): ValidationIssue[] {
  return result.blockers.filter((b) => SOFT_BLOCKER_CODES.has(b.code));
}

/**
 * List all non-cancelled assignments for a resource within a time window.
 */
export async function getAssignmentsByResource(
  resourceId: string,
  from: Date,
  to: Date,
): Promise<ResourceAssignmentInfo[]> {
  const params = new URLSearchParams({
    from: from.toISOString(),
    to: to.toISOString(),
  });
  return apiGet<ResourceAssignmentInfo[]>(`${API_PATHS.resourceAssignments(resourceId)}?${params}`);
}

/**
 * List all non-cancelled assignments for every resource of a type within a time
 * window, in one request. Replaces the per-person assignment fan-out in the
 * People grid. Callers group the flat result by `resourceId` client-side.
 */
export async function getAssignmentsByResourceType(
  resourceTypeKey: string,
  from: Date,
  to: Date,
): Promise<ResourceAssignmentInfo[]> {
  const params = new URLSearchParams({
    resourceTypeKey,
    from: from.toISOString(),
    to: to.toISOString(),
  });
  return apiGet<ResourceAssignmentInfo[]>(`${API_PATHS.RESOURCE_ASSIGNMENTS}?${params}`);
}

/**
 * List resource assignments for a request
 */
export async function getAssignmentsByRequest(requestId: string): Promise<ResourceAssignmentInfo[]> {
  return apiGet<ResourceAssignmentInfo[]>(`${API_PATHS.RESOURCE_ASSIGNMENTS}?requestId=${encodeURIComponent(requestId)}`);
}

/**
 * Validate a resource assignment without persisting it
 */
export async function validateAssignment(request: ValidateResourceAssignmentRequest): Promise<ValidationResult> {
  return apiPost<ValidationResult>(API_PATHS.RESOURCE_ASSIGNMENTS_VALIDATE, request);
}

/** One entry of a batch validation response; echoes ids for correlation. */
export interface AssignmentValidationBatchItem {
  requestId?: string;
  resourceId: string;
  result: ValidationResult;
}

/**
 * Validate many assignment pairings in a single round-trip. Used by the conflicts
 * view, which evaluates every scheduled request at once. Results are correlated
 * back to inputs by `requestId`/`resourceId`, not by position.
 */
export async function validateAssignmentsBatch(
  items: ValidateResourceAssignmentRequest[],
): Promise<AssignmentValidationBatchItem[]> {
  if (items.length === 0) return [];
  return apiPost<AssignmentValidationBatchItem[]>(
    API_PATHS.RESOURCE_ASSIGNMENTS_VALIDATE_BATCH,
    { items },
  );
}

/**
 * Create a resource assignment
 */
export async function createAssignment(request: CreateResourceAssignmentRequest): Promise<ResourceAssignmentInfo> {
  return apiPost<ResourceAssignmentInfo>(API_PATHS.RESOURCE_ASSIGNMENTS, request);
}

/**
 * Cancel (delete) a resource assignment
 */
export async function cancelAssignment(id: string): Promise<void> {
  return apiDelete(API_PATHS.resourceAssignment(id));
}
