import { apiPost } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

// ── Types ───────────────────────────────────────────────────────────

export type SolverKind = "Greedy" | "OrToolsCpSat";
export type SolverStatus = "Optimal" | "Feasible" | "Infeasible" | "Unknown";

export type SchedulingReasonCode =
  | "NoCompatibleResource"
  | "InsufficientCapacity"
  | "BlockedByFixedAssignments"
  | "InvalidDuration"
  | "InternalSolverLimit"
  | "PredecessorUnscheduled";

export interface AutoSchedulePreviewRequest {
  siteId: string;
  horizonStart: string;
  horizonEnd: string;
  requestIds?: string[];
  respectSchedulingSettings?: boolean;
  /**
   * Which resource types this run fills. A request is placed with one resource of every type
   * it needs from this set, all at the same time. Omit to fill every type a request can
   * target. Apply inherits it and must repeat the value the preview used, or the backend
   * re-solves for a different set than the one the user saw.
   */
  resourceTypeKeys?: string[];
}

export interface AutoScheduleApplyRequest extends AutoSchedulePreviewRequest {
  previewFingerprint?: string;
}

export interface AutoScheduleScore {
  scheduledCount: number;
  unscheduledCount: number;
  priorityScore: number;
}

export interface ProposedResourceDto {
  typeKey: string;
  resourceId: string;
  resourceName: string;
}

export interface ProposedAssignmentDto {
  requestId: string;
  requestName: string;
  /** One resource per type the request needed, all occupied together. */
  resources: ProposedResourceDto[];
  /** ISO-8601 UTC timestamps: the half-open window the apply writes. */
  start: string;
  end: string;
  /** Working minutes inside the window. */
  durationMinutes: number;
}

export interface UnscheduledRequestDto {
  requestId: string;
  requestName: string;
  reasonCodes: SchedulingReasonCode[];
}

export interface AutoSchedulePreviewResponse {
  solverUsed: SolverKind;
  status: SolverStatus;
  score: AutoScheduleScore;
  assignments: ProposedAssignmentDto[];
  unscheduled: UnscheduledRequestDto[];
  diagnostics: string[];
  fingerprint: string;
}

export interface AutoScheduleApplyResponse {
  createdAssignments: number;
  unscheduledCount: number;
}

// ── API calls ───────────────────────────────────────────────────────

export async function previewAutoSchedule(
  request: AutoSchedulePreviewRequest,
): Promise<AutoSchedulePreviewResponse> {
  return apiPost<AutoSchedulePreviewResponse>(
    API_PATHS.AUTO_SCHEDULE_PREVIEW,
    request,
  );
}

export async function applyAutoSchedule(
  request: AutoScheduleApplyRequest,
): Promise<AutoScheduleApplyResponse> {
  return apiPost<AutoScheduleApplyResponse>(
    API_PATHS.AUTO_SCHEDULE_APPLY,
    request,
  );
}
