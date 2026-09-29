/**
 * Generic API client utility for DRY HTTP requests
 *
 * All requests go through a single `apiFetch` function that applies
 * credentials, headers (including CSRF for mutations), and error handling.
 */

import { API_BASE_URL, getApiHeaders, handleApiError } from '../core/api-utils';

export type QueryParams = Record<string, string | number | boolean | undefined>;

export interface ApiRequestOptions {
  /** Additional headers to merge with default headers */
  headers?: Record<string, string>;
  /** Header names to remove after merging (e.g. strip tenant slug for site-scope calls) */
  omitHeaders?: string[];
  /** Query parameters to append to URL; `undefined` values are left out */
  params?: QueryParams;
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

export async function apiGet<T>(
  endpoint: string,
  options?: ApiRequestOptions
): Promise<T> {
  const url = buildUrl(endpoint, options?.params);
  const response = await apiFetch(url, 'GET', options);
  return response.json();
}

/** POST/PUT/PATCH share one path: send JSON, then parse the body unless there is none. */
async function sendJson<TResponse>(
  method: 'POST' | 'PUT' | 'PATCH',
  endpoint: string,
  data: unknown,
  options?: ApiRequestOptions,
): Promise<TResponse> {
  const url = buildUrl(endpoint, options?.params);
  const response = await apiFetch(url, method, options, data);

  // A 204 has no body, so parsing one throws and turns a successful write into a
  // rejected promise — the caller's onSuccess never runs and the UI reports a failure
  // for a save that happened.
  if (options?.skipJsonParse || response.status === 204) {
    return undefined as TResponse;
  }

  return response.json();
}

export function apiPost<TResponse>(
  endpoint: string,
  data: unknown,
  options?: ApiRequestOptions
): Promise<TResponse> {
  return sendJson('POST', endpoint, data, options);
}

export function apiPut<TResponse>(
  endpoint: string,
  data: unknown,
  options?: ApiRequestOptions
): Promise<TResponse> {
  return sendJson('PUT', endpoint, data, options);
}

/** `data`, when given, is sent as a JSON body (e.g. a password re-check on a destructive DELETE). */
export async function apiDelete(
  endpoint: string,
  options?: ApiRequestOptions,
  data?: unknown,
): Promise<void> {
  const url = buildUrl(endpoint, options?.params);
  await apiFetch(url, 'DELETE', options, data);
}

export function apiPatch<TResponse>(
  endpoint: string,
  data: unknown,
  options?: ApiRequestOptions
): Promise<TResponse> {
  return sendJson('PATCH', endpoint, data, options);
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
 * Build full URL with base and optional query parameters.
 *
 * Exported for the one caller that cannot go through the wrappers above: the assistant's
 * chat turn POSTs and reads a stream, so it owns its own `fetch`. It must still resolve
 * its URL here — a bare relative path is same-origin, which is wrong whenever
 * API_BASE_URL points the API at another origin.
 */
export function buildUrl(endpoint: string, params?: QueryParams): string {
  // When API_BASE_URL is empty (subdomain mode), use same-origin
  const base = API_BASE_URL || window.location.origin;
  const url = new URL(endpoint, base);

  if (params) {
    Object.entries(params).forEach(([key, value]) => {
      if (value !== undefined) url.searchParams.append(key, String(value));
    });
  }

  return url.toString();
}
