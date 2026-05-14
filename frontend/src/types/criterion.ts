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
}

export interface UpdateCriterionRequest {
  description?: string;
  enumValues?: string[];
  unit?: string;
  applicableToRequests?: boolean;
}
