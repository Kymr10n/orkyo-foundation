/**
 * API client for Global Search operations
 */

import { apiGet } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

export interface SearchResultOpen {
  route: string;
  params: Record<string, string>;
}

export interface SearchResultPermissions {
  canRead: boolean;
  canEdit: boolean;
}

export interface SearchResult {
  type: 'space' | 'request' | 'group' | 'site' | 'template' | 'criterion';
  id: string;
  title: string;
  subtitle?: string;
  siteId?: string;
  score: number;
  updatedAt: string;
  open: SearchResultOpen;
  permissions: SearchResultPermissions;
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
 * Search across all entities (spaces, requests, groups, sites, templates, criteria)
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
