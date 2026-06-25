/**
 * Auto-schedule contract types and reason taxonomy shared across product compositions.
 *
 * These values define the preview/apply result shape exposed by scheduling features.
 */

export type SolverKind = "Greedy" | "OrToolsCpSat";

export type SolverStatus = "Optimal" | "Feasible" | "Infeasible" | "Unknown";

export type SchedulingReasonCode =
  | "NoCompatibleSpace"
  | "InsufficientCapacity"
  | "BlockedByFixedAssignments"
  | "InvalidDuration"
  | "InternalSolverLimit";

export const SchedulingReasonLabels: Record<SchedulingReasonCode, string> = {
  NoCompatibleSpace: "No compatible space",
  InsufficientCapacity: "Insufficient capacity",
  BlockedByFixedAssignments: "Blocked by existing assignments",
  InvalidDuration: "Invalid duration",
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
  resourceId: string;
  resourceName: string;
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
