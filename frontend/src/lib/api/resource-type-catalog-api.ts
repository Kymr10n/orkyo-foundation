/**
 * API client for the resource type catalog.
 *
 * The catalog is the fixed set of pre-configured manufacturing types a tenant can switch
 * on under Configuration → Type catalog. No type is built in: activating an entry creates
 * (or re-activates) an ordinary, fully editable tenant type with the entry's preset custom
 * fields; deactivating hides it while its data survives; purging deletes the type together
 * with its resources and everything that references them.
 */

import { API_PATHS } from '../core/api-paths';
import { apiGet, apiPost, apiRawFetch } from '../core/api-client';
import type { ResourceTypeInfo } from './resource-types-api';

/** Tenant-side state of a catalog entry, derived by key. */
export type CatalogEntryState = 'active' | 'inactive' | 'absent';

export type CatalogCategory = 'Stationary' | 'Mobile';

export interface CatalogEntry {
  key: string;
  displayName: string;
  displayNamePlural: string;
  description: string;
  /** lucide-react icon name; resolved through the curated allow-list. */
  icon: string;
  category: CatalogCategory;
  hasGeometry: boolean;
  hasDirectoryProfile: boolean;
  singleGroupMembership: boolean;
  /** Labels of the fields activation ships, in form order. */
  fieldLabels: string[];
  state: CatalogEntryState;
  /** The tenant's row for this key, when one exists. */
  resourceTypeId?: string | null;
  /** The tenant's current display name — activation adopts the row, renames survive. */
  tenantDisplayName?: string | null;
  resourceCount: number;
  requestTargetCount: number;
}

/** What a purge removed, for the success message. */
export interface CatalogPurgeResult {
  resources: number;
  assignments: number;
  groups: number;
  requestTargets: number;
}

export function getResourceTypeCatalog(): Promise<CatalogEntry[]> {
  return apiGet<CatalogEntry[]>(API_PATHS.RESOURCE_TYPE_CATALOG);
}

/** Activates a catalog type, adopting the tenant's existing row when one exists. */
export function activateCatalogType(key: string): Promise<ResourceTypeInfo> {
  return apiPost<ResourceTypeInfo>(API_PATHS.resourceTypeCatalogActivate(key), undefined);
}

/** Deactivates a catalog type, keeping its data — the Hide path. */
export function deactivateCatalogType(key: string): Promise<void> {
  return apiPost<void>(API_PATHS.resourceTypeCatalogDeactivate(key), undefined, {
    skipJsonParse: true,
  });
}

/** Deletes a catalog type together with its resources and related data. */
export async function purgeCatalogType(key: string): Promise<CatalogPurgeResult> {
  // apiRawFetch, not apiDelete: this DELETE returns the counts as a JSON body.
  const response = await apiRawFetch(API_PATHS.resourceTypeCatalogEntry(key), 'DELETE');
  return response.json();
}
