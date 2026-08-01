export type CriterionDataType = 'Boolean' | 'Number' | 'String' | 'Enum';

/** Runtime value for a criterion — depends on the criterion's dataType. */
export type CriterionValue = boolean | number | string;

export interface Criterion {
  id: string;
  name: string;
  description?: string;
  dataType: CriterionDataType;
  enumValues?: string[];
  unit?: string;
  applicableToRequests?: boolean; // Phase 3: defaults to true
  resourceTypeKeys: string[];
  /** True when the criterion has value assignments; data type is locked while in use. */
  inUse?: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CreateCriterionRequest {
  name: string;
  description?: string;
  dataType: CriterionDataType;
  enumValues?: string[];
  unit?: string;
  applicableToRequests?: boolean;
  resourceTypeKeys: string[];
}

export interface UpdateCriterionRequest {
  name?: string;
  description?: string;
  enumValues?: string[];
  unit?: string;
  dataType?: CriterionDataType;
}

export interface CriterionApplicabilityInfo {
  criterionId: string;
  applicableToRequests: boolean;
  resourceTypeKeys: string[];
}

export interface UpdateCriterionApplicabilityRequest {
  resourceTypeKeys?: string[];
  applicableToRequests?: boolean;
}
