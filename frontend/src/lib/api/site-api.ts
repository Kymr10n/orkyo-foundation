/**
 * API client for Site operations
 */

import { API_PATHS } from "../core/api-paths";
import type { Site, CreateSiteRequest, UpdateSiteRequest } from "@foundation/src/types/site";
import { createCrudApi } from "./create-crud-api";

export type { Site };

const sitesApi = createCrudApi<Site, CreateSiteRequest, UpdateSiteRequest>({
  collectionPath: API_PATHS.SITES,
  itemPath: API_PATHS.site,
});

/**
 * Get all sites for the current tenant
 */
export async function getSites(): Promise<Site[]> {
  return sitesApi.list();
}

/**
 * Create a new site
 */
export async function createSite(request: CreateSiteRequest): Promise<Site> {
  return sitesApi.create(request);
}

/**
 * Update an existing site
 */
export async function updateSite(
  siteId: string,
  request: UpdateSiteRequest,
): Promise<Site> {
  return sitesApi.update(siteId, request);
}

/**
 * Delete a site
 */
export async function deleteSite(siteId: string): Promise<void> {
  return sitesApi.remove(siteId);
}
