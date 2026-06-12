/**
 * API client for Request CRUD operations
 */

import type {
  CreateRequestRequest,
  MoveRequestRequest,
  Request,
  UpdateRequestRequest,
} from "@foundation/src/types/requests";
import { apiGet, apiPost, apiPut, apiDelete, apiPatch } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

/**
 * Get all requests, optionally with their requirements hydrated.
 *
 * The list endpoint omits requirements by default for payload economy; pass
 * `includeRequirements` to opt in (e.g. the Requests page, which renders them).
 * Conflict detection no longer relies on this — capability checks are evaluated
 * by the backend validator, not reconstructed client-side.
 */
export async function getRequests(
  includeRequirements = false,
): Promise<Request[]> {
  const path = includeRequirements
    ? `${API_PATHS.REQUESTS}?includeRequirements=true`
    : API_PATHS.REQUESTS;
  return apiGet<Request[]>(path);
}

/**
 * Get only requests that currently have ≥1 conflict (tenant-wide). Backs the Conflicts page so it
 * loads just the conflicted rows rather than the whole tenant.
 */
export async function getConflictedRequests(): Promise<Request[]> {
  return apiGet<Request[]>(`${API_PATHS.REQUESTS}?conflicted=true`);
}

/**
 * Get the direct children of a request. Used to decide whether a request may be
 * converted to a leaf (Task) — the backend rejects that while children exist.
 */
export async function getRequestChildren(requestId: string): Promise<Request[]> {
  return apiGet<Request[]>(API_PATHS.requestChildren(requestId));
}

/**
 * Create a new request
 */
export async function createRequest(
  request: CreateRequestRequest,
): Promise<Request> {
  return apiPost<Request>(API_PATHS.REQUESTS, request);
}

/**
 * Update an existing request
 */
export async function updateRequest(
  requestId: string,
  request: UpdateRequestRequest,
): Promise<Request> {
  return apiPut<Request>(API_PATHS.request(requestId), request);
}

/**
 * Delete a request
 */
export async function deleteRequest(requestId: string): Promise<void> {
  return apiDelete(API_PATHS.request(requestId));
}

/**
 * Move/reparent a request in the tree
 */
export async function moveRequest(
  requestId: string,
  request: MoveRequestRequest,
): Promise<Request> {
  return apiPatch<Request>(
    API_PATHS.requestMove(requestId),
    request,
  );
}

/**
 * Delete a request and all its descendants
 */
export async function deleteRequestSubtree(
  requestId: string,
): Promise<void> {
  return apiDelete(API_PATHS.requestSubtree(requestId));
}
