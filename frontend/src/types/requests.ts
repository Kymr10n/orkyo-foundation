import type { CriterionValue, ResourceTypeKey } from './criterion';

export type { ResourceTypeKey };
export type AssignmentStatus = 'Planned' | 'Confirmed' | 'Tentative' | 'Cancelled';

export interface ResourceAssignment {
  id: string;
  resourceId: string;
  resourceTypeKey: ResourceTypeKey;
  startUtc: string;
  endUtc: string;
  allocationPercent?: number | null;
  allocationUnits?: number | null;
  assignmentStatus: AssignmentStatus;
  createdAt: string;
  updatedAt: string;
  /** True for client-side optimistic assignments that have not yet been confirmed by the server. */
  isOptimistic?: boolean;
}

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

export type RequestStatus = "new" | "in_progress" | "done" | "cancelled" | "deferred";

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
  /** For `overlap` conflicts: the id of the other request that this one overlaps with. */
  peerRequestId?: string;
  /** The assigned resource (space/person/tool) this conflict is about, when it maps to one —
   *  lets the editor flag the specific row. Absent for request-level conflicts (e.g. timing). */
  resourceId?: string;
  /** For capability conflicts: the unmet requirement's criterion — flags that requirement row. */
  criterionId?: string;
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

  /** Site this request is scoped to. null = site-neutral (schedulable at any site). */
  siteId?: string | null;

  // Item reference
  requestItemId?: string | null;

  // All resource assignments for this request
  assignments: ResourceAssignment[];

  // Display icon (string ID from REQUEST_ICONS, resolved on the FE)
  icon?: string | null;

  // Scheduling fields (nullable until scheduled)
  startTs?: string | null; // ISO timestamp
  endTs?: string | null; // ISO timestamp

  // Scheduling constraints (optional)
  earliestStartTs?: string | null; // ISO timestamp - earliest time request can start
  latestEndTs?: string | null; // ISO timestamp - latest time request must end by

  minimalDurationValue: number;
  minimalDurationUnit: DurationUnit;

  // Actual scheduled duration (set when a leaf request is scheduled)
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
  operator?: string; // Phase 3: ">=", "<=", "=" for Number criteria
  allowedValues?: CriterionValue[]; // Phase 3: Set of allowed values for Enum criteria
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
  siteId?: string | null;
  resourceId?: string;
  requestItemId?: string;
  icon?: string | null;
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
    operator?: string; // Phase 3: ">=", "<=", "=" for Number
    allowedValues?: CriterionValue[]; // Phase 3: Set of allowed values for Enum
  }[];
}

export interface UpdateRequestRequest {
  name?: string;
  description?: string;
  parentRequestId?: string;
  planningMode?: PlanningMode;
  sortOrder?: number;
  siteId?: string | null;
  /** When true, a null siteId is applied (clears to "any site") rather than preserved. */
  changeSiteId?: boolean;
  resourceId?: string;
  requestItemId?: string;
  icon?: string | null;
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
    operator?: string; // Phase 3: ">=", "<=", "=" for Number
    allowedValues?: CriterionValue[]; // Phase 3: Set of allowed values for Enum
  }[];
}

export interface MoveRequestRequest {
  newParentRequestId?: string | null;
  sortOrder: number;
}

/**
 * The request form's submit payload (RequestFormDialog → onSave). Lives here so
 * lib-level payload builders don't import from the components layer.
 */
export interface RequestFormData {
  name: string;
  description?: string;
  icon?: string | null;
  planningMode: PlanningMode;
  parentRequestId?: string;
  /** Site scope. null/undefined = site-neutral (Any site). */
  siteId?: string | null;
  resourceId?: string;
  startTs?: string;
  endTs?: string;
  earliestStartTs?: string;
  latestEndTs?: string;
  duration: Duration;
  schedulingSettingsApply: boolean;
  requirements: {
    criterionId: string;
    value: CriterionValue | null;
    operator?: string;
  }[];
}
