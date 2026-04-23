import { apiPost } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

// ── Types ───────────────────────────────────────────────────────────

export type AutoScheduleMode = "FillGapsOnly";
export type SolverKind = "Greedy" | "OrToolsCpSat";
export type SolverStatus = "Optimal" | "Feasible" | "Infeasible" | "Unknown" | "Error";

export type SchedulingReasonCode =
  | "None"
  | "NoCompatibleSpace"
  | "DateWindowTooTight"
  | "InsufficientCapacity"
  | "BlockedByFixedAssignments"
  | "InvalidDuration"
  | "MissingRequiredData"
  | "InternalSolverLimit";

export interface AutoSchedulePreviewRequest {
  siteId: string;
  horizonStart: string;
  horizonEnd: string;
  mode?: AutoScheduleMode;
  requestIds?: string[];
  respectSchedulingSettings?: boolean;
}

export interface AutoScheduleApplyRequest extends AutoSchedulePreviewRequest {
  previewFingerprint?: string;
}

export interface AutoScheduleScore {
  scheduledCount: number;
  unscheduledCount: number;
  priorityScore: number;
}

export interface ProposedAssignmentDto {
  requestId: string;
  requestName: string;
  spaceId: string;
  spaceName: string;
  start: string;
  end: string;
  durationDays: number;
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
