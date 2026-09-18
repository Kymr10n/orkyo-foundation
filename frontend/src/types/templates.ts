export interface TemplateItem {
  id: string;
  templateId: string;
  criterionId: string;
  value: string;
}

export interface Template {
  id: string;
  name: string;
  description?: string;
  entityType: 'request' | 'space' | 'group';
  durationValue?: number;
  durationUnit?: string;
  /** Request templates: the resource types a request made from it needs. */
  targetResourceTypeKeys?: string[];
  items?: TemplateItem[];
  createdAt?: string;
  updatedAt?: string;
}

export interface CreateTemplateRequest {
  name: string;
  description?: string;
  entityType: 'request' | 'space' | 'group';
  durationValue?: number;
  durationUnit?: string;
  targetResourceTypeKeys?: string[];
  items?: TemplateItem[];
}

export interface UpdateTemplateRequest {
  name?: string;
  description?: string;
  entityType?: 'request' | 'space' | 'group';
  durationValue?: number;
  durationUnit?: string;
  /** Omit to leave the types as they are; an empty list clears them. */
  targetResourceTypeKeys?: string[];
  items?: TemplateItem[];
}
