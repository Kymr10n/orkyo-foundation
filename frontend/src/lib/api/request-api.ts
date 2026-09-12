/**
 * API client for Request CRUD operations
 */

import type {
  CreateRequestRequest,
  DurationUnit,
  MoveRequestRequest,
  Request,
  UpdateRequestRequest,
} from "@foundation/src/types/requests";
import { DEFAULT_DURATION_VALUE, DEFAULT_DURATION_UNIT } from "@foundation/src/constants/app";
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
  siteId?: string,
): Promise<Request[]> {
  const params = new URLSearchParams();
  if (includeRequirements) params.set("includeRequirements", "true");
  // Site-neutral requests stay in the result — the backend keeps them under every site.
  if (siteId) params.set("siteId", siteId);
  const query = params.toString();
  return apiGet<Request[]>(query ? `${API_PATHS.REQUESTS}?${query}` : API_PATHS.REQUESTS);
}

/**
 * Get one request by id. For surfaces that hold only an id and need the whole request on
 * demand — the Bottlenecks critical path lists nodes by id, and opening one must not cost a
 * tenant-wide list read.
 */
export async function getRequest(id: string): Promise<Request> {
  return apiGet<Request>(`${API_PATHS.REQUESTS}/${id}`);
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
 * Create a leaf task under a parent, from nothing but a name.
 *
 * The quick paths into a group — the Children tab and the sequence editor — both ask for a name
 * and nothing else, so the remaining required fields come from one place rather than from
 * whichever screen happens to be open. A task created this way is a starting point: duration and
 * scheduling are edited afterwards, in the request itself.
 */
export async function createChildRequest(
  parentRequestId: string,
  name: string,
  sortOrder: number,
): Promise<Request> {
  return createRequest({
    parentRequestId,
    name,
    planningMode: "leaf",
    sortOrder,
    minimalDurationValue: DEFAULT_DURATION_VALUE,
    minimalDurationUnit: DEFAULT_DURATION_UNIT as DurationUnit,
  });
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
