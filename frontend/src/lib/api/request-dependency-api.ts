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
  /** ISO-8601 UTC timestamps at minute precision. */
  earliestStart: string;
  earliestFinish: string;
  latestStart: string;
  latestFinish: string;
  /** Minutes of slack. Zero means any delay here delays everything downstream. */
  totalFloatMinutes: number;
  isCritical: boolean;
  isScheduled: boolean;
  chainId: string;
}

/** One connected group of dependent requests, measured against its own finish. */
export interface CriticalPathChain {
  chainId: string;
  /** The chain's requests in dependency order. */
  requestIds: string[];
  firstName: string;
  lastName: string;
  start: string;
  finish: string;
  /** The earliest deadline in the chain, or null when no request carries one. */
  deadline: string | null;
  /** Minutes from the earliest finish to the deadline. Negative means late; null without a deadline. */
  slackMinutes: number | null;
}

export interface CriticalPathResult {
  nodes: CriticalPathNode[];
  edges: RequestDependency[];
  /** Most at risk first: least slack to a deadline, then earliest finish. */
  chains: CriticalPathChain[];
  diagnostics: string[];
}

export function getCriticalPath(siteId?: string | null): Promise<CriticalPathResult> {
  return apiGet<CriticalPathResult>(API_PATHS.REQUEST_CRITICAL_PATH, {
    params: siteId ? { siteId } : undefined,
  });
}
