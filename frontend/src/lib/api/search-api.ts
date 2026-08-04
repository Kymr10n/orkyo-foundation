/**
 * API client for Global Search operations
 */

import { apiGet } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

export interface SearchResultPermissions {
  canRead: boolean;
  canEdit: boolean;
}

export interface SearchResult {
  /**
   * Every resource — space, person, tool, or a tenant-defined type — indexes as 'resource';
   * the specific type is in resourceTypeKey. The other members are entities that are not
   * resources, so the list no longer grows when a tenant defines a type.
   */
  type: 'resource' | 'request' | 'group' | 'site' | 'template' | 'criterion';
  id: string;
  title: string;
  subtitle?: string;
  siteId?: string;
  score: number;
  updatedAt: string;
  permissions: SearchResultPermissions;
  /**
   * For 'resource' results, the type key — used to route and label. For 'group' results, the
   * type the group holds, selecting which page owns it. Absent for everything else.
   */
  resourceTypeKey?: string;
}

export interface SearchResponse {
  query: string;
  results: SearchResult[];
}

interface SearchParams {
  query: string;
  siteId?: string;
  types?: string[];
  limit?: number;
}

/**
 * Search across all entities (resources, requests, groups, sites, templates, criteria)
 */
export async function globalSearch(params: SearchParams): Promise<SearchResponse> {
  const queryParams: Record<string, string> = {
    q: params.query,
  };

  if (params.siteId) {
    queryParams.siteId = params.siteId;
  }

  if (params.types && params.types.length > 0) {
    queryParams.types = params.types.join(',');
  }

  if (params.limit) {
    queryParams.limit = params.limit.toString();
  }

  return apiGet<SearchResponse>(API_PATHS.SEARCH, { params: queryParams });
}
