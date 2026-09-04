import type { CriterionValue } from './criterion';

export type AssignmentStatus = 'Planned' | 'Confirmed' | 'Tentative' | 'Cancelled';

/**
 * How a request joins its incoming dependencies — which predecessors must be met before it may
 * start.
 * - `all` — every predecessor (the default, and what every request did before conditions existed)
 * - `any` — one is enough
 * - `k_of_n` — at least `predecessorLogicK` of them
 *
 * Cancelled and deferred predecessors leave the set before the condition is judged, so scrapped
 * work cannot hold a task shut forever.
 */
export type PredecessorLogic = 'all' | 'any' | 'k_of_n';

export interface ResourceAssignment {
  id: string;
  resourceId: string;
  resourceTypeKey: string;
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
    | "resource_unavailable"
    | "insufficient_working_time"
    | "capacity_exceeded"
    | "dependency_violation";
  severity: "warning" | "error";
  message: string;
  /**
   * For `overlap` conflicts: the id of the other request that this one overlaps with.
   * For `dependency_violation`: the predecessor this request waits for.
   */
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

  /**
   * How this request joins its predecessors. The server always sends it; optional here so a
   * fixture need not restate the default, and absent reads as `all` — the same default the
   * column carries, and what every request did before conditions existed.
   */
  predecessorLogic?: PredecessorLogic;
  /** The k of k_of_n; null or absent for the other logics. */
  predecessorLogicK?: number | null;

  /** Site this request is scoped to. null = site-neutral (schedulable at any site). */
  siteId?: string | null;

  // Item reference
  requestItemId?: string | null;

  // All resource assignments for this request
  assignments: ResourceAssignment[];

  /**
   * The resource types this request needs, one assignment each. Always sent by the backend
   * (sorted); optional here only because older fixtures predate it — read it through
   * `getTargetResourceTypeKeys()`, which reads it in one place. Optional here only because
   * payloads are built by hand in tests; there is no default any more, so an absent value
   * means the request targets nothing.
   */
  targetResourceTypeKeys?: string[];

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
  /** One resource per targeted type. Sent together so a multi-type request cannot be left
   *  half-assigned by a follow-up call failing. */
  resourceIds?: string[];
  requestItemId?: string;
  /** Omit to target spaces. An empty list is a real state: a request needing no resource. */
  targetResourceTypeKeys?: string[];
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
  /**
   * The join condition. The pair travels together: sending predecessorLogic rewrites k as well
   * (cleared unless k_of_n), so a stale k cannot survive a switch to all/any. Omit both to leave
   * the condition untouched.
   */
  predecessorLogic?: PredecessorLogic;
  predecessorLogicK?: number | null;
  /** One resource per targeted type, replacing whatever holds each type's slot. */
  resourceIds?: string[];
  requestItemId?: string;
  /** Omit to leave the targets untouched; a supplied list replaces them wholesale. */
  targetResourceTypeKeys?: string[];
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
  resourceIds?: string[];
  targetResourceTypeKeys: string[];
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
