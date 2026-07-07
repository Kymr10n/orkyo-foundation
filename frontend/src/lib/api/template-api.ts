import { apiGet } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import type { Template, CreateTemplateRequest, UpdateTemplateRequest } from '@foundation/src/types/templates';
import { createCrudApi } from './create-crud-api';

const templatesApi = createCrudApi<Template, CreateTemplateRequest, Partial<UpdateTemplateRequest>>({
  collectionPath: API_PATHS.TEMPLATES,
  itemPath: API_PATHS.template,
});

/**
 * Get all templates for a specific entity type
 */
export async function getTemplates(entityType: 'request' | 'space' | 'group'): Promise<Template[]> {
  return apiGet<Template[]>(API_PATHS.templatesWithType(entityType));
}

/**
 * Create a new template
 */
export async function createTemplate(request: CreateTemplateRequest): Promise<Template> {
  return templatesApi.create(request);
}

/**
 * Update an existing template
 */
export async function updateTemplate(id: string, request: Partial<UpdateTemplateRequest>): Promise<Template> {
  return templatesApi.update(id, request);
}

/**
 * Delete a template
 */
export async function deleteTemplate(id: string): Promise<void> {
  return templatesApi.remove(id);
}
