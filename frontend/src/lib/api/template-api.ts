import { apiDelete, apiGet, apiPost, apiPut } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';
import type { Template, CreateTemplateRequest, UpdateTemplateRequest } from '@/types/templates';

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
  return apiPost<Template>(API_PATHS.TEMPLATES, request);
}

/**
 * Update an existing template
 */
export async function updateTemplate(id: string, request: Partial<UpdateTemplateRequest>): Promise<Template> {
  return apiPut<Template>(API_PATHS.template(id), request);
}

/**
 * Delete a template
 */
export async function deleteTemplate(id: string): Promise<void> {
  return apiDelete(API_PATHS.template(id));
}
