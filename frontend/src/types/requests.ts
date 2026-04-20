import type { CriterionValue } from './criterion';

export type DurationUnit =
  | "years"
  | "months"
  | "weeks"
  | "days"
  | "hours"
  | "minutes";

export interface Duration {
  value: number;
  unit: DurationUnit;
}

export type RequestStatus = "planned" | "in_progress" | "done" | "cancelled";

export interface Conflict {
  id: string;
  kind:
    | "connector_mismatch"
    | "load_exceeded"
    | "size_mismatch"
    | "overlap"
    | "below_min_duration"
    | "before_earliest_start"
    | "after_latest_end"
    | "starts_in_off_time"
    | "insufficient_working_time"
    | "capacity_exceeded";
  severity: "warning" | "error";
  message: string;
}

export type PlanningMode = "leaf" | "summary" | "container";

/**
 * Request entity as returned by the backend API (matches BE RequestInfo).
 * Fields marked "FE-computed" are enriched client-side and NOT sent by the API.
 */
export interface Request {
  id: string;
  name: string;
  description?: string | null;

  // Tree hierarchy
  parentRequestId?: string | null;
  planningMode: PlanningMode;
  sortOrder: number;
  
  // Space and item references
  spaceId?: string | null;
  requestItemId?: string | null;

  // Scheduling fields (nullable until scheduled)
  startTs?: string | null; // ISO timestamp
  endTs?: string | null; // ISO timestamp
  
  // Scheduling constraints (optional)
  earliestStartTs?: string | null; // ISO timestamp - earliest time request can start
  latestEndTs?: string | null; // ISO timestamp - latest time request must end by
  
  minimalDurationValue: number;
  minimalDurationUnit: DurationUnit;
  
  // Actual scheduled duration (when allocated to a space)
  actualDurationValue?: number | null;
  actualDurationUnit?: DurationUnit | null;

  // Scheduling settings
  schedulingSettingsApply: boolean;

  status: RequestStatus;
  requirements?: RequestRequirement[];
  createdAt: string;
  updatedAt: string;

  // Computed by backend
  isScheduled?: boolean;

  // FE-computed: duration in minutes, calculated from minimalDuration fields by utilization-api
  durationMin?: number;
}

export interface RequestRequirement {
  id: string;
  requestId: string;
  criterionId: string;
  value: CriterionValue;
  createdAt?: string;
  criterion?: {
    id: string;
    name: string;
    dataType: string;
    unit?: string;
    enumValues?: string[];
  };
}

// API request/response types
export interface CreateRequestRequest {
  name: string;
  description?: string;
  parentRequestId?: string;
  planningMode?: PlanningMode;
  sortOrder?: number;
  spaceId?: string;
  requestItemId?: string;
  startTs?: string;
  endTs?: string;
  earliestStartTs?: string;
  latestEndTs?: string;
  minimalDurationValue: number;
  minimalDurationUnit: DurationUnit;
  actualDurationValue?: number;
  actualDurationUnit?: DurationUnit;
  schedulingSettingsApply?: boolean;
  status?: RequestStatus;
  requirements?: {
    criterionId: string;
    value: CriterionValue;
  }[];
}

export interface UpdateRequestRequest {
  name?: string;
  description?: string;
  parentRequestId?: string;
  planningMode?: PlanningMode;
  sortOrder?: number;
  spaceId?: string;
  requestItemId?: string;
  startTs?: string;
  endTs?: string;
  earliestStartTs?: string;
  latestEndTs?: string;
  minimalDurationValue?: number;
  minimalDurationUnit?: DurationUnit;
  actualDurationValue?: number;
  actualDurationUnit?: DurationUnit;
  schedulingSettingsApply?: boolean;
  status?: RequestStatus;
  requirements?: {
    criterionId: string;
    value: CriterionValue;
  }[];
}

export interface MoveRequestRequest {
  newParentRequestId?: string | null;
  sortOrder: number;
}

