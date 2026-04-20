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
  items?: TemplateItem[];
}

export interface UpdateTemplateRequest {
  name?: string;
  description?: string;
  entityType?: 'request' | 'space' | 'group';
  durationValue?: number;
  durationUnit?: string;
  items?: TemplateItem[];
}