/**
 * API client for Site operations
 */

import { apiDelete, apiGet, apiPost, apiPut } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";
import type { Site, CreateSiteRequest, UpdateSiteRequest } from "@foundation/src/types/site";

export type { Site };

/**
 * Get all sites for the current tenant
 */
export async function getSites(): Promise<Site[]> {
  return apiGet<Site[]>(API_PATHS.SITES);
}

/**
 * Create a new site
 */
export async function createSite(request: CreateSiteRequest): Promise<Site> {
  return apiPost<Site>(API_PATHS.SITES, request);
}

/**
 * Update an existing site
 */
export async function updateSite(
  siteId: string,
  request: UpdateSiteRequest,
): Promise<Site> {
  return apiPut<Site>(API_PATHS.site(siteId), request);
}

/**
 * Delete a site
 */
export async function deleteSite(siteId: string): Promise<void> {
  return apiDelete(API_PATHS.site(siteId));
}
