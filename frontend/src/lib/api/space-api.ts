/**
 * API client for Space CRUD operations
 */

import type { Space, CreateSpaceRequest, UpdateSpaceRequest } from '@foundation/src/types/space';
import { apiGet, apiPost, apiPut, apiDelete } from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

/**
 * Get all spaces for a site
 */
export async function getSpaces(siteId: string): Promise<Space[]> {
  return apiGet<Space[]>(API_PATHS.spaces(siteId));
}

/**
 * Create a new space
 */
export async function createSpace(siteId: string, request: CreateSpaceRequest): Promise<Space> {
  return apiPost<Space>(API_PATHS.spaces(siteId), request);
}

/**
 * Update an existing space
 */
export async function updateSpace(
  siteId: string,
  spaceId: string,
  request: UpdateSpaceRequest
): Promise<Space> {
  return apiPut<Space>(API_PATHS.space(siteId, spaceId), request);
}

/**
 * Delete a space
 */
export async function deleteSpace(siteId: string, spaceId: string): Promise<void> {
  return apiDelete(API_PATHS.space(siteId, spaceId));
}
