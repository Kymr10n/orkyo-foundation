/**
 * API client for Request CRUD operations
 */

import type {
  CreateRequestRequest,
  MoveRequestRequest,
  Request,
  UpdateRequestRequest,
} from "@/types/requests";
import { apiGet, apiPost, apiPut, apiDelete, apiPatch } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

/**
 * Get all requests, optionally with requirements included
 */
export async function getRequests(
  _includeRequirements = false,
): Promise<Request[]> {
  return apiGet<Request[]>(API_PATHS.REQUESTS);
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
