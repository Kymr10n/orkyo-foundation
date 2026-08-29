/**
 * API client for precedence edges between requests.
 *
 * Dependencies are independent of the request tree: containment says what a request is part of,
 * this says what has to happen first, and the two routinely disagree.
 */

import { apiGet, apiPost, apiDelete } from "../core/api-client";
import { API_PATHS } from "../core/api-paths";

export interface RequestDependency {
  id: string;
  predecessorRequestId: string;
  successorRequestId: string;
  /** Peer names travel with the edge so a list needs no second fetch. */
  predecessorName: string;
  successorName: string;
  dependencyType: string;
  /** Minimum gap after the predecessor finishes. Minutes; the UI shows hours. */
  lagMinutes: number;
  createdAt: string;
}

/** The edges touching one request, split by direction. */
export interface RequestDependencies {
  predecessors: RequestDependency[];
  successors: RequestDependency[];
}

export function getRequestDependencies(requestId: string): Promise<RequestDependencies> {
  return apiGet<RequestDependencies>(API_PATHS.requestDependencies(requestId));
}

/** Makes `requestId` wait for `predecessorRequestId` to finish. */
export function addRequestDependency(
  requestId: string,
  predecessorRequestId: string,
  lagMinutes = 0,
): Promise<RequestDependency> {
  return apiPost<RequestDependency>(API_PATHS.requestDependencies(requestId), {
    predecessorRequestId,
    lagMinutes,
  });
}

export function deleteRequestDependency(requestId: string, dependencyId: string): Promise<void> {
  return apiDelete(API_PATHS.requestDependency(requestId, dependencyId));
}

// ── Critical path ───────────────────────────────────────────────────────────

export interface CriticalPathNode {
  requestId: string;
  name: string;
  earliestStart: string;
  earliestFinish: string;
  latestStart: string;
  latestFinish: string;
  /** Days of slack. Zero means any delay here delays everything downstream. */
  totalFloatDays: number;
  isCritical: boolean;
  isScheduled: boolean;
}

export interface CriticalPathResult {
  nodes: CriticalPathNode[];
  edges: RequestDependency[];
  durationDays: number;
  diagnostics: string[];
}

export function getCriticalPath(siteId?: string | null): Promise<CriticalPathResult> {
  return apiGet<CriticalPathResult>(API_PATHS.REQUEST_CRITICAL_PATH, {
    params: siteId ? { siteId } : undefined,
  });
}
