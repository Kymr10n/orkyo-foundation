/**
 * Generic API client utility for DRY HTTP requests
 * 
 * All requests go through a single `apiFetch` function that applies
 * credentials, headers (including CSRF for mutations), and error handling.
 */

import { API_BASE_URL, getApiHeaders, handleApiError } from '../core/api-utils';

export interface ApiRequestOptions {
  /** Additional headers to merge with default headers */
  headers?: Record<string, string>;
  /** Header names to remove after merging (e.g. strip tenant slug for site-scope calls) */
  omitHeaders?: string[];
  /** Query parameters to append to URL */
  params?: Record<string, string | number | boolean>;
  /** Optional Request cache mode override */
  cache?: RequestCache;
  /** Whether to skip automatic JSON parsing (for void returns) */
  skipJsonParse?: boolean;
}

/** Build final header set: defaults + overrides - omits */
function buildRequestHeaders(method: string, options?: ApiRequestOptions): Record<string, string> {
  const headers = { ...getApiHeaders(method), ...options?.headers };
  if (options?.omitHeaders) {
    for (const key of options.omitHeaders) {
      // eslint-disable-next-line @typescript-eslint/no-dynamic-delete
      delete headers[key];
    }
  }
  return headers;
}

/**
 * Core fetch wrapper — single place for credentials, headers, and error handling.
 * All public API methods delegate here.
 */
async function apiFetch(
  url: string,
  method: string,
  options?: ApiRequestOptions,
  body?: unknown,
): Promise<Response> {
  const init: RequestInit = {
    method,
    headers: buildRequestHeaders(method, options),
    credentials: 'include',
  };
  if (options?.cache !== undefined) {
    init.cache = options.cache;
  } else if (method === 'GET') {
    init.cache = 'no-store';
  }
  if (body !== undefined) {
    init.body = JSON.stringify(body);
  }
  const response = await fetch(url, init);
  if (!response.ok) {
    await handleApiError(response);
  }
  return response;
}

/**
 * Generic GET request
 */
export async function apiGet<T>(
  endpoint: string,
  options?: ApiRequestOptions
): Promise<T> {
  const url = buildUrl(endpoint, options?.params);
  const response = await apiFetch(url, 'GET', options);
  return response.json();
}

/**
 * Generic POST request
 */
export async function apiPost<TResponse>(
  endpoint: string,
  data: unknown,
  options?: ApiRequestOptions
): Promise<TResponse> {
  const url = buildUrl(endpoint, options?.params);
  const response = await apiFetch(url, 'POST', options, data);

  if (options?.skipJsonParse || response.status === 204) {
    return undefined as TResponse;
  }

  return response.json();
}

/**
 * Generic PUT request
 */
export async function apiPut<TResponse>(
  endpoint: string,
  data: unknown,
  options?: ApiRequestOptions
): Promise<TResponse> {
  const url = buildUrl(endpoint, options?.params);
  const response = await apiFetch(url, 'PUT', options, data);
  return response.json();
}

/**
 * Generic DELETE request
 */
export async function apiDelete(
  endpoint: string,
  options?: ApiRequestOptions
): Promise<void> {
  const url = buildUrl(endpoint, options?.params);
  await apiFetch(url, 'DELETE', options);
}

/**
 * Generic PATCH request
 */
export async function apiPatch<TResponse>(
  endpoint: string,
  data: unknown,
  options?: ApiRequestOptions
): Promise<TResponse> {
  const url = buildUrl(endpoint, options?.params);
  const response = await apiFetch(url, 'PATCH', options, data);
  return response.json();
}

/**
 * Raw fetch with BFF credentials + standard headers, for non-JSON responses
 * (e.g. blobs, streams). Caller handles the Response directly.
 */
export async function apiRawFetch(
  endpoint: string,
  method = 'GET',
  options?: ApiRequestOptions,
): Promise<Response> {
  const url = buildUrl(endpoint, options?.params);
  return apiFetch(url, method, options);
}

/**
 * Build full URL with base and optional query parameters
 */
function buildUrl(endpoint: string, params?: Record<string, string | number | boolean>): string {
  // When API_BASE_URL is empty (subdomain mode), use same-origin
  const base = API_BASE_URL || window.location.origin;
  const url = new URL(endpoint, base);
  
  if (params) {
    Object.entries(params).forEach(([key, value]) => {
      url.searchParams.append(key, String(value));
    });
  }
  
  return url.toString();
}

/**
 * Legacy helper for building endpoint paths
 */
export function endpoint(...parts: (string | number)[]): string {
  return parts.join('/');
}
