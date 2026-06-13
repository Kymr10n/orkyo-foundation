export type CriterionDataType = 'Boolean' | 'Number' | 'String' | 'Enum';

/** Runtime value for a criterion — depends on the criterion's dataType. */
export type CriterionValue = boolean | number | string;

/** Resource-type keys this criterion applies to. 'tool' exists in the DB schema but tools
 *  management is not yet built — keep the key so stored data round-trips correctly. */
export type ResourceTypeKey = 'space' | 'person' | 'tool';

export interface Criterion {
  id: string;
  name: string;
  description?: string;
  dataType: CriterionDataType;
  enumValues?: string[];
  unit?: string;
  applicableToRequests?: boolean; // Phase 3: defaults to true
  resourceTypeKeys: ResourceTypeKey[];
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
  resourceTypeKeys: ResourceTypeKey[];
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
  resourceTypeKeys: ResourceTypeKey[];
}

export interface UpdateCriterionApplicabilityRequest {
  resourceTypeKeys?: ResourceTypeKey[];
  applicableToRequests?: boolean;
}
