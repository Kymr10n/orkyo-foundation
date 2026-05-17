export type CriterionDataType = 'Boolean' | 'Number' | 'String' | 'Enum';

/** Runtime value for a criterion — depends on the criterion's dataType. */
export type CriterionValue = boolean | number | string;

/** Resource-type keys this criterion applies to. */
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
  description?: string;
  enumValues?: string[];
  unit?: string;
  applicableToRequests?: boolean;
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
