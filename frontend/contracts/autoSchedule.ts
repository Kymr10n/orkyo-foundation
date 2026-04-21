/**
 * Auto-schedule contract types and reason taxonomy shared across product compositions.
 *
 * These values define the preview/apply result shape exposed by scheduling features.
 */

export type SolverKind = "Greedy" | "OrToolsCpSat";

export type SolverStatus =
  | "Optimal"
  | "Feasible"
  | "Infeasible"
  | "Unknown"
  | "Error";

export type SchedulingReasonCode =
  | "None"
  | "NoCompatibleSpace"
  | "DateWindowTooTight"
  | "InsufficientCapacity"
  | "BlockedByFixedAssignments"
  | "InvalidDuration"
  | "MissingRequiredData"
  | "InternalSolverLimit";

export const SchedulingReasonLabels: Record<SchedulingReasonCode, string> = {
  None: "None",
  NoCompatibleSpace: "No compatible space",
  DateWindowTooTight: "Date window too tight",
  InsufficientCapacity: "Insufficient capacity",
  BlockedByFixedAssignments: "Blocked by existing assignments",
  InvalidDuration: "Invalid duration",
  MissingRequiredData: "Missing required data",
  InternalSolverLimit: "Solver limit reached",
};

export function formatSolverKind(kind: SolverKind): string {
  return kind === "OrToolsCpSat" ? "OR-Tools CP-SAT" : "Greedy";
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
